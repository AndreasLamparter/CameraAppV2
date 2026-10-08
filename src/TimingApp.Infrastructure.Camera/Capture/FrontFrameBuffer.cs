using TimingApp.Application.FinishRecording;

namespace TimingApp.Infrastructure.Camera.Capture;

/// <summary>
/// Ring buffer of compressed front frames, bounded by time (retention). Thread-safe: written by the compression task,
/// read by the recording saver and the preview. Completed when the session stops; remains readable afterwards.
/// </summary>
internal sealed class FrontFrameBuffer(TimeSpan retention) : IFrontFrameBuffer
{
    private readonly Lock _lock = new();
    private readonly LinkedList<EncodedFrame> _frames = new();
    private readonly List<(DateTimeOffset Timestamp, TaskCompletionSource Signal)> _waiters = [];
    private bool _completed;

    public EncodedFrame? Latest
    {
        get
        {
            lock (_lock)
            {
                return _frames.Last?.Value;
            }
        }
    }

    public void Add(EncodedFrame frame)
    {
        List<TaskCompletionSource>? release = null;
        lock (_lock)
        {
            _frames.AddLast(frame);
            while (_frames.First is { } first && frame.Timestamp - first.Value.Timestamp > retention)
            {
                _frames.RemoveFirst();
            }
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (frame.Timestamp >= _waiters[i].Timestamp)
                {
                    (release ??= []).Add(_waiters[i].Signal);
                    _waiters.RemoveAt(i);
                }
            }
        }
        release?.ForEach(s => s.TrySetResult());
    }

    /// <summary>No more frames will come; releases all waiters.</summary>
    public void Complete()
    {
        List<TaskCompletionSource> release;
        lock (_lock)
        {
            _completed = true;
            release = _waiters.Select(w => w.Signal).ToList();
            _waiters.Clear();
        }
        release.ForEach(s => s.TrySetResult());
    }

    public IReadOnlyList<EncodedFrame> Snapshot(DateTimeOffset from, DateTimeOffset to)
    {
        lock (_lock)
        {
            return TimeRange.Covering(_frames.ToList(), f => f.Timestamp, from, to);
        }
    }

    public async Task WaitForAsync(DateTimeOffset timestamp, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_completed || _frames.Last?.Value.Timestamp >= timestamp)
            {
                return;
            }
            _waiters.Add((timestamp, signal));
        }
        try
        {
            await signal.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            lock (_lock)
            {
                _waiters.RemoveAll(w => w.Signal == signal);
            }
        }
    }
}
