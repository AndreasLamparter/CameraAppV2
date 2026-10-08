using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;
using TimingApp.Domain.SharedKernel;

namespace TimingApp.Application.Tests;

/// <summary>
/// Column channel whose reader reports when the consumer has processed everything written so far: the processing
/// loop handles each item before asking for the next, so a failed read means all earlier items are done.
/// </summary>
internal sealed class ObservableColumnReader(ChannelReader<LineColumn> inner) : ChannelReader<LineColumn>
{
    private readonly Lock _lock = new();
    private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override bool CanCount => true;

    public override int Count => inner.Count;

    public override Task Completion => inner.Completion;

    public override bool TryRead([MaybeNullWhen(false)] out LineColumn item)
    {
        if (inner.TryRead(out item))
        {
            return true;
        }
        lock (_lock)
        {
            _idle.TrySetResult();
        }
        return false;
    }

    public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) => inner.WaitToReadAsync(cancellationToken);

    /// <summary>Call before writing; the returned task completes once the consumer has processed those writes.</summary>
    public Task NextIdle()
    {
        lock (_lock)
        {
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _idle.Task;
        }
    }
}

internal sealed class FakeFrontBuffer : IFrontFrameBuffer
{
    private readonly List<EncodedFrame> _frames = [];

    public bool Completed { get; set; }

    public void Add(EncodedFrame frame)
    {
        lock (_frames)
        {
            _frames.Add(frame);
        }
    }

    public IReadOnlyList<EncodedFrame> Snapshot(DateTimeOffset from, DateTimeOffset to)
    {
        lock (_frames)
        {
            return TimeRange.Covering(_frames.ToList(), f => f.Timestamp, from, to);
        }
    }

    public Task WaitForAsync(DateTimeOffset timestamp, TimeSpan timeout, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeCameraSession : ICameraSession
{
    private readonly Channel<LineColumn> _channel = Channel.CreateUnbounded<LineColumn>();

    public FakeCameraSession(bool frontEnabled)
    {
        Reader = new ObservableColumnReader(_channel.Reader);
        Front = frontEnabled ? new FakeFrontBuffer() : null;
    }

    public ObservableColumnReader Reader { get; }

    public FakeFrontBuffer? Front { get; }

    public CameraHealth FinishHealth { get; set; } = new(CameraState.Running, null, 90);

    public CameraHealth FrontHealth { get; set; } = new(CameraState.Running, null, 30);

    public bool Disposed { get; private set; }

    public ChannelReader<LineColumn> Columns => Reader;

    public IFrontFrameBuffer? FrontFrames => Front;

    public CameraHealth Health(CameraRole role) => role == CameraRole.Finish ? FinishHealth : Front is null ? new CameraHealth(CameraState.Disabled, null, 0) : FrontHealth;

    /// <summary>Writes columns and waits until the processing loop has handled all of them.</summary>
    public async Task FeedAsync(IEnumerable<LineColumn> columns)
    {
        var idle = Reader.NextIdle();
        foreach (var column in columns)
        {
            await _channel.Writer.WriteAsync(column);
        }
        await idle.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _channel.Writer.TryComplete();
        if (Front is not null)
        {
            Front.Completed = true;
        }
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeCameraSystem : ICameraSystem
{
    public List<CameraSessionOptions> Starts { get; } = [];

    public FakeCameraSession? Session { get; private set; }

    public CameraHealth? FrontHealth { get; set; }

    public Task<ICameraSession> StartAsync(CameraSessionOptions options, CancellationToken cancellationToken)
    {
        Starts.Add(options);
        Session = new FakeCameraSession(options.FrontCamera is not null);
        if (FrontHealth is { } health)
        {
            Session.FrontHealth = health;
        }
        return Task.FromResult<ICameraSession>(Session);
    }

    public Task<IReadOnlyList<CameraDevice>> ListDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CameraDevice>>([]);
}

internal sealed class FakeRenderer : IFinishImageRenderer
{
    public Task<FinishImageInfo> RenderAsync(IReadOnlyList<LineColumn> columns, bool reverseTimeDirection, string path, CancellationToken cancellationToken) =>
        Task.FromResult(new FinishImageInfo(columns.Count, (columns.Count > 0 ? columns[0].Bgr.Length / 3 : 0) + 36, 36));
}

internal sealed class FakeEncoder : IVideoEncoder
{
    public string? FailWith { get; set; }

    /// <summary>When set, encoding waits for it (a save in progress).</summary>
    public TaskCompletionSource? Gate { get; set; }

    public List<IReadOnlyList<EncodedFrame>> FrontVideos { get; } = [];

    public async Task<Result> EncodeFinishVideoAsync(string imagePath, FinishImageInfo image, double lineRate, bool reverseTimeDirection, string outputPath, CancellationToken cancellationToken)
    {
        if (Gate is { } gate)
        {
            await gate.Task;
        }
        return FailWith is null ? Result.Success() : Error.Unavailable(FailWith);
    }

    public Task<Result> EncodeFrontVideoAsync(IReadOnlyList<EncodedFrame> frames, double frameRate, string outputPath, CancellationToken cancellationToken)
    {
        FrontVideos.Add(frames);
        return Task.FromResult(FailWith is null ? Result.Success() : Error.Unavailable(FailWith));
    }
}

internal sealed class InMemoryRecordingStore : IRecordingStore
{
    private readonly List<RecordingMetadata> _complete = [];

    public List<RecordingTarget> Created { get; } = [];

    public IReadOnlyList<RecordingMetadata> Complete
    {
        get
        {
            lock (_complete)
            {
                return _complete.ToList();
            }
        }
    }

    public Task<RecordingTarget> CreateAsync(DateTimeOffset start, string? mediaDirectory, CancellationToken cancellationToken)
    {
        var id = RecordingId.For(start, Created.Count(t => t.Id.Value.StartsWith(RecordingId.For(start).Value, StringComparison.Ordinal)));
        var target = new RecordingTarget(id, $"media/{id}", $"media/{id}/finish.png", $"media/{id}/finish.mp4", $"media/{id}/front.mp4");
        Created.Add(target);
        return Task.FromResult(target);
    }

    public Task CompleteAsync(RecordingTarget target, RecordingMetadata metadata, CancellationToken cancellationToken)
    {
        lock (_complete)
        {
            _complete.Add(metadata);
        }
        return Task.CompletedTask;
    }

    public Task AbandonAsync(RecordingTarget target, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<RecordingSummary>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecordingSummary>>(Complete.Select(m => new RecordingSummary(m.Id, m.StartedAt, 0, m.LineRate, m.FrontVideo?.Available == true, false, 0)).ToList());

    public Task<Result<RecordingMetadata>> GetAsync(RecordingId id, CancellationToken cancellationToken) =>
        Task.FromResult<Result<RecordingMetadata>>(Complete.FirstOrDefault(m => m.Id == id.Value) is { } m ? m : Error.NotFound("recording.notFound"));

    public Task<Result<string>> GetFileAsync(RecordingId id, RecordingFile file, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>(Error.NotFound("recording.fileNotFound"));

    public Task<Result> DeleteAsync(RecordingId id, CancellationToken cancellationToken)
    {
        lock (_complete)
        {
            return Task.FromResult(_complete.RemoveAll(m => m.Id == id.Value) > 0 ? Result.Success() : Result.Failure(Error.NotFound("recording.notFound")));
        }
    }
}

internal sealed class InMemorySettingsStore : ISettingsStore
{
    public FinishRecordingSettings Settings { get; set; } = FinishRecordingSettings.Default;

    public Task<FinishRecordingSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);

    public Task SaveAsync(FinishRecordingSettings settings, CancellationToken cancellationToken)
    {
        Settings = settings;
        return Task.CompletedTask;
    }
}

internal sealed class FakeNotifier : IFinishRecordingNotifier
{
    private int _recordingsChanged;

    public int RecordingsChangedCount => Volatile.Read(ref _recordingsChanged);

    public void StatusChanged()
    {
    }

    public void RecordingsChanged() => Interlocked.Increment(ref _recordingsChanged);
}

internal sealed class FixedMediaDirectory : IMediaDirectoryResolver
{
    public string Resolve(string? configured) => configured ?? "media";
}

/// <summary>The application under test with fakes at all ports.</summary>
internal sealed class FinishRecordingHarness : IAsyncDisposable
{
    public FinishRecordingHarness()
    {
        Saver = new RecordingSaver(Store, Renderer, Encoder, Notifier, TimeProvider.System, NullLogger<RecordingSaver>.Instance);
        Service = new FinishRecordingService(Cameras, Settings, Saver, Notifier, TimeProvider.System, NullLogger<FinishRecordingService>.Instance);
    }

    public FakeCameraSystem Cameras { get; } = new();

    public InMemorySettingsStore Settings { get; } = new();

    public InMemoryRecordingStore Store { get; } = new();

    public FakeRenderer Renderer { get; } = new();

    public FakeEncoder Encoder { get; } = new();

    public FakeNotifier Notifier { get; } = new();

    public RecordingSaver Saver { get; }

    public FinishRecordingService Service { get; }

    public FakeCameraSession Session => Cameras.Session ?? throw new InvalidOperationException("Cameras not started.");

    public async Task WaitForRecordingsAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Store.Complete.Count < count || Saver.Pending > 0)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Expected {count} recordings, got {Store.Complete.Count}.");
            }
            await Task.Delay(10);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync();
        await Saver.DisposeAsync();
    }
}

/// <summary>Synthetic finish-line columns: a gray background and a dark rider covering half of the line.</summary>
internal static class Line
{
    public const int Length = 200;
    public static readonly DateTimeOffset TenOClock = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);

    public static byte[] Pixels(bool occupied)
    {
        var bgr = new byte[Length * 3];
        for (var i = 0; i < Length; i++)
        {
            var value = occupied && i is >= 50 and < 150 ? (byte)20 : (byte)120;
            bgr[i * 3] = bgr[(i * 3) + 1] = bgr[(i * 3) + 2] = value;
        }
        return bgr;
    }

    /// <summary>Columns at <paramref name="rate"/> per second, aligned to 10:00:00, for [from, to) seconds relative to 10:00:00.</summary>
    public static IEnumerable<LineColumn> Columns(double fromSeconds, double toSeconds, Func<double, bool> occupied, int rate = 90)
    {
        var free = Pixels(false);
        var rider = Pixels(true);
        for (var i = (long)Math.Ceiling(fromSeconds * rate); i < (long)Math.Ceiling(toSeconds * rate); i++)
        {
            var seconds = (double)i / rate;
            yield return new LineColumn(TenOClock.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond)), occupied(seconds) ? rider : free);
        }
    }

    public static Func<double, bool> Between(double from, double to) => s => s >= from - 1e-9 && s <= to + 1e-9;

    public static readonly Func<double, bool> Free = _ => false;
}
