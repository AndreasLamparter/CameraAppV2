using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Application.FinishRecording;

/// <summary>
/// A completed finish event to be saved, with its columns, access to the front frames, its race (FS2-03) and its
/// passages (FS2-14), which may still grow after saving.
/// </summary>
public sealed record RecordingDraft(
    FinishEvent Event,
    RecordingWindow Window,
    IReadOnlyList<LineColumn> Columns,
    IFrontFrameBuffer? FrontFrames,
    FinishRecordingSettings Settings,
    RaceName? Race = null,
    RecordingPassages? Passages = null);

/// <summary>
/// Saves recordings one after another, decoupled from the column processing: waits for the front post-roll, renders
/// the finish image, encodes the videos and writes the metadata last (FS1-40 to FS1-43). A failed video keeps the
/// finish image and timestamps and is reported as <see cref="LastProblem"/>.
/// </summary>
public sealed class RecordingSaver : IAsyncDisposable
{
    /// <summary>Extra time to wait for the front frames of the post-roll before saving with what is there.</summary>
    public static readonly TimeSpan FrontWaitMargin = TimeSpan.FromSeconds(3);

    // Drafts to save and passage updates of saved recordings, in order; one reader writes all files.
    private readonly Channel<object> _queue = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
    private readonly IRecordingStore _store;
    private readonly IFinishImageRenderer _renderer;
    private readonly IVideoEncoder _encoder;
    private readonly IFinishRecordingNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly ILogger<RecordingSaver> _logger;
    private readonly Task _loop;
    private int _pending;
    private volatile SaveProblem? _lastProblem;

    public RecordingSaver(
        IRecordingStore store,
        IFinishImageRenderer renderer,
        IVideoEncoder encoder,
        IFinishRecordingNotifier notifier,
        TimeProvider time,
        ILogger<RecordingSaver> logger)
    {
        _store = store;
        _renderer = renderer;
        _encoder = encoder;
        _notifier = notifier;
        _time = time;
        _logger = logger;
        _loop = Task.Run(RunAsync);
    }

    /// <summary>Recordings queued or being saved.</summary>
    public int Pending => Volatile.Read(ref _pending);

    public SaveProblem? LastProblem => _lastProblem;

    public void Enqueue(RecordingDraft draft) => EnqueueJob(draft);

    private void EnqueueJob(object job)
    {
        Interlocked.Increment(ref _pending);
        if (!_queue.Writer.TryWrite(job))
        {
            Interlocked.Decrement(ref _pending);
            throw new InvalidOperationException("The recording saver has been shut down.");
        }
        _notifier.StatusChanged();
    }

    /// <summary>A passage arrived after its recording was saved: rewrite the passages of the metadata (FS2-11).</summary>
    private void EnqueuePassageUpdate(RecordingId id, IReadOnlyList<Passage> passages)
    {
        try
        {
            EnqueueJob(new PassageUpdate(id, passages));
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning("Passages of recording {RecordingId} not updated: saver shut down", id);
        }
    }

    /// <summary>Completes when every queued recording has been saved (FS1-50: before the application ends).</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (Pending > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), _time, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _loop.ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await (job is RecordingDraft draft ? SaveAsync(draft, CancellationToken.None) : UpdateAsync((PassageUpdate)job)).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // One failed recording must not stop saving the following ones; the failure is reported.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Saving {Job} failed", job is RecordingDraft d ? $"the recording of the finish event at {d.Event.StartedAt:O}" : "passages");
                _lastProblem = new SaveProblem("recording.saveFailed", null, _time.GetUtcNow());
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
                _notifier.StatusChanged();
            }
        }
    }

    private async Task SaveAsync(RecordingDraft draft, CancellationToken cancellationToken)
    {
        if (draft.Columns.Count == 0)
        {
            return;
        }
        var settings = draft.Settings;
        var frames = await CollectFrontFramesAsync(draft, cancellationToken).ConfigureAwait(false);

        var target = await _store.CreateAsync(draft.Columns[0].Timestamp, settings.MediaDirectory, draft.Race, cancellationToken).ConfigureAwait(false);
        FinishImageInfo image;
        try
        {
            image = await _renderer.RenderAsync(draft.Columns, settings.FinishLine.ReverseTimeDirection, target.FinishImagePath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await _store.AbandonAsync(target, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var lineRate = RateOf(draft.Columns.Select(c => c.Timestamp).ToList());
        var finishVideo = await _encoder.EncodeFinishVideoAsync(
            target.FinishImagePath,
            image,
            lineRate > 0 ? lineRate : settings.FinishCamera.FrameRate,
            settings.FinishLine.ReverseTimeDirection,
            target.FinishVideoPath,
            cancellationToken).ConfigureAwait(false);

        FrontVideoInfo? frontVideo = null;
        if (frames.Count >= 2)
        {
            var frameRate = RateOf(frames.Select(f => f.Timestamp).ToList());
            var encoded = await _encoder.EncodeFrontVideoAsync(frames, frameRate, target.FrontVideoPath, cancellationToken).ConfigureAwait(false);
            frontVideo = new FrontVideoInfo(encoded.IsSuccess, encoded.Error?.Code, frameRate, frames.Select(f => UnixMicroseconds.From(f.Timestamp)).ToList());
        }

        var passages = draft.Passages?.Snapshot() ?? [];
        var metadata = new RecordingMetadata(
            target.Id.Value,
            draft.Columns[0].Timestamp,
            draft.Columns[^1].Timestamp,
            draft.Event.StartedAt,
            draft.Event.EndedAt,
            draft.Event.EndReason,
            lineRate,
            settings.FinishLine.ReverseTimeDirection,
            image.Width,
            image.Height,
            image.TimelineHeight,
            draft.Columns.Select(c => UnixMicroseconds.From(c.Timestamp)).ToList(),
            new VideoInfo(finishVideo.IsSuccess, finishVideo.Error?.Code),
            frontVideo,
            settings.FinishCamera.OffsetMs,
            settings.FrontCamera.OffsetMs,
            draft.Race?.Value,
            ToInfo(passages));
        await _store.CompleteAsync(target, metadata, cancellationToken).ConfigureAwait(false);
        if (draft.Passages?.MarkSaved(target.Id, passages.Count, EnqueuePassageUpdate) is { } arrivedMeanwhile)
        {
            EnqueuePassageUpdate(target.Id, arrivedMeanwhile);
        }

        var videoError = finishVideo.Error?.Code ?? frontVideo?.ErrorCode;
        if (videoError is not null)
        {
            _logger.LogWarning("Recording {RecordingId} saved without video: {ErrorCode}", target.Id, videoError);
            _lastProblem = new SaveProblem(videoError, target.Id.Value, _time.GetUtcNow());
        }
        else
        {
            _logger.LogInformation("Recording {RecordingId} saved ({Columns} columns, {FrontFrames} front frames)", target.Id, draft.Columns.Count, frames.Count);
        }
        _notifier.RecordingsChanged();
    }

    private async Task UpdateAsync(PassageUpdate update)
    {
        var result = await _store.UpdatePassagesAsync(update.Id, ToInfo(update.Passages), CancellationToken.None).ConfigureAwait(false);
        if (result.IsFailure)
        {
            _logger.LogWarning("Passages of recording {RecordingId} not updated: {Code}", update.Id, result.Error!.Code);
            return;
        }
        _logger.LogInformation("Recording {RecordingId} now has {Passages} passages", update.Id, update.Passages.Count);
        _notifier.RecordingsChanged();
    }

    private static List<PassageInfo> ToInfo(IReadOnlyList<Passage> passages) =>
        [.. passages.OrderBy(p => p.Time).Select(p => new PassageInfo(p.StartNumber, UnixMicroseconds.From(p.Time)))];

    private sealed record PassageUpdate(RecordingId Id, IReadOnlyList<Passage> Passages);

    private static async Task<IReadOnlyList<EncodedFrame>> CollectFrontFramesAsync(RecordingDraft draft, CancellationToken cancellationToken)
    {
        if (draft.FrontFrames is not { } buffer)
        {
            return [];
        }
        var timeout = draft.Settings.Detection.FrontPostRoll + FrontWaitMargin;
        await buffer.WaitForAsync(draft.Window.FrontEnd, timeout, cancellationToken).ConfigureAwait(false);
        return buffer.Snapshot(draft.Window.FrontStart, draft.Window.FrontEnd);
    }

    /// <summary>Measured rate of a recording: intervals per second between the first and the last timestamp.</summary>
    private static double RateOf(List<DateTimeOffset> timestamps)
    {
        if (timestamps.Count < 2)
        {
            return 0;
        }
        var seconds = (timestamps[^1] - timestamps[0]).TotalSeconds;
        return seconds > 0 ? (timestamps.Count - 1) / seconds : 0;
    }
}
