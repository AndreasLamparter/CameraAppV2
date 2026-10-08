using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;
using TimingApp.Infrastructure.Camera.Preview;

namespace TimingApp.Infrastructure.Camera.Capture;

/// <summary>
/// Decodes finish camera JPEGs on several workers, so the frame rate is not limited by one core: each worker decodes a
/// frame, takes the finish-line column and offers the preview. Columns are handed to <c>emit</c> in capture order
/// (sequence numbers, reordered under a lock). The capture thread only posts; when all workers are busy and the
/// bounded queue is full the frame is dropped and counted, the capture thread never waits.
/// </summary>
internal sealed class ParallelFinishDecoder
{
    private readonly Channel<(long Sequence, byte[] Jpeg, DateTimeOffset Timestamp)> _queue;
    private readonly FinishLineSettings _line;
    private readonly Action<LineColumn> _emit;
    private readonly FramePreview? _preview;
    private readonly ILogger _logger;
    private readonly Task[] _workers;
    private readonly Lock _orderLock = new();
    private readonly SortedDictionary<long, LineColumn?> _done = [];
    private long _nextPosted;
    private long _nextEmitted;
    private long _dropped;

    public ParallelFinishDecoder(int workers, FinishLineSettings line, Action<LineColumn> emit, FramePreview? preview, ILogger logger)
    {
        _line = line;
        _emit = emit;
        _preview = preview;
        _logger = logger;
        _queue = Channel.CreateBounded<(long, byte[], DateTimeOffset)>(new BoundedChannelOptions(workers * 4)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _workers = [.. Enumerable.Range(0, workers).Select(_ => Task.Run(WorkAsync))];
    }

    /// <summary>Number of worker threads for this machine: half the logical processors, 2 to 4.</summary>
    public static int DefaultWorkers => Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    /// <summary>Frames dropped because decoding could not keep up.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Called on the capture thread only; never blocks.</summary>
    public void Post(byte[] jpeg, DateTimeOffset timestamp)
    {
        if (_queue.Writer.TryWrite((_nextPosted, jpeg, timestamp)))
        {
            _nextPosted++;
            return;
        }
        if (Interlocked.Increment(ref _dropped) % 90 == 1)
        {
            _logger.LogWarning("Finish frame decoding cannot keep up; {Dropped} frames dropped", Dropped);
        }
    }

    /// <summary>No more frames: lets the workers finish the queue; all decoded columns have been emitted afterwards.</summary>
    public async Task CompleteAsync()
    {
        _queue.Writer.TryComplete();
        await Task.WhenAll(_workers).ConfigureAwait(false);
    }

    private async Task WorkAsync()
    {
        await foreach (var (sequence, jpeg, timestamp) in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            LineColumn? column = null;
            try
            {
                using var frame = Cv2.ImDecode(jpeg, ImreadModes.Color);
                if (!frame.Empty())
                {
                    column = new LineColumn(timestamp, LineExtractor.Extract(frame, _line));
                    _preview?.Offer(frame);
                }
            }
#pragma warning disable CA1031 // A corrupt frame is skipped; the sequence must still advance.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogDebug(ex, "Finish frame {Sequence} could not be decoded", sequence);
            }
            Done(sequence, column);
        }
    }

    /// <summary>Emits all columns that are next in capture order; a failed frame only advances the sequence.</summary>
    private void Done(long sequence, LineColumn? column)
    {
        lock (_orderLock)
        {
            _done[sequence] = column;
            while (_done.Remove(_nextEmitted, out var next))
            {
                if (next is not null)
                {
                    _emit(next);
                }
                _nextEmitted++;
            }
        }
    }
}
