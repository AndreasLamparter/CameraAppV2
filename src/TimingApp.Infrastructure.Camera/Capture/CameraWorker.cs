using Microsoft.Extensions.Logging;
using OpenCvSharp;
using TimingApp.Application.FinishRecording;
using TimingApp.Infrastructure.Camera.Sources;

namespace TimingApp.Infrastructure.Camera.Capture;

/// <summary>Receives frames on the capture thread. Must be fast and non-blocking and must not keep <paramref name="frame"/>.</summary>
internal interface IFrameSink
{
    void OnFrame(CapturedFrame frame);
}

/// <summary>
/// The frame just grabbed, valid only during <see cref="IFrameSink.OnFrame"/>: its timestamp, the camera's JPEG (when
/// it delivers one) and decoding on demand into the capture thread's buffer, so sinks only pay for what they use.
/// </summary>
internal sealed class CapturedFrame(ICameraSource source, Mat buffer)
{
    public DateTimeOffset Timestamp { get; internal set; }

    /// <summary>A copy of the camera's JPEG, or null when the camera delivers another format.</summary>
    public byte[]? CopyJpeg() => source.CopyJpeg();

    /// <summary>The decoded BGR frame (owned by the capture thread, not to be kept), or null when decoding failed.</summary>
    public Mat? Decode() => source.Retrieve(buffer) && !buffer.Empty() ? buffer : null;
}

/// <summary>
/// One dedicated capture thread per camera: opens the source, grabs frames, timestamps them right after acquisition on
/// the shared capture clock plus the camera offset, measures the frame rate and hands frames to the sink, which
/// decides whether to decode them. The thread owns the source and the frame buffer and disposes them when it ends.
/// </summary>
internal sealed class CameraWorker
{
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMilliseconds(20);

    private readonly CameraRole _role;
    private readonly ICameraSource _source;
    private readonly CaptureClock _clock;
    private readonly TimeSpan _offset;
    private readonly IFrameSink _sink;
    private readonly TimeSpan _noFramesTimeout;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly FrameRateMeter _rate;
    private volatile bool _stopRequested;
    private volatile CameraState _state = CameraState.Starting;
    private volatile string? _errorCode;
    private volatile string? _warningCode;
    private long _startedAt;
    private long _lastFrameAt;

    public CameraWorker(CameraRole role, ICameraSource source, CaptureClock clock, TimeSpan offset, IFrameSink sink, TimeSpan noFramesTimeout, ILogger logger)
    {
        _role = role;
        _source = source;
        _clock = clock;
        _offset = offset;
        _sink = sink;
        _noFramesTimeout = noFramesTimeout;
        _logger = logger;
        _rate = new FrameRateMeter(clock);
    }

    /// <summary>Completes when the capture thread has ended and released the device.</summary>
    public Task Stopped => _stopped.Task;

    public void Start()
    {
        _startedAt = _clock.Timestamp();
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = $"camera-{_role}",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
    }

    public void RequestStop() => _stopRequested = true;

    public CameraHealth Health()
    {
        var state = _state;
        if (state == CameraState.Running)
        {
            var last = Interlocked.Read(ref _lastFrameAt);
            var silentFor = _clock.Elapsed(last == 0 ? _startedAt : last);
            if (silentFor > _noFramesTimeout)
            {
                return new CameraHealth(CameraState.Error, "camera.noFrames", 0);
            }
        }
        return new CameraHealth(state, _errorCode, state == CameraState.Running ? _rate.Rate : 0) { WarningCode = _warningCode };
    }

    private void Run()
    {
        try
        {
            if (!TryOpen())
            {
                return;
            }
            _warningCode = _source.Warning;
            _state = CameraState.Running;
            _logger.LogInformation("Camera {Role} capturing", _role);
            using var buffer = new Mat();
            var frame = new CapturedFrame(_source, buffer);
            while (!_stopRequested)
            {
                if (!_source.Grab())
                {
                    Thread.Sleep(FailureBackoff);
                    continue;
                }
                frame.Timestamp = _clock.Now() + _offset;
                Interlocked.Exchange(ref _lastFrameAt, _clock.Timestamp());
                _rate.Tick();
                _sink.OnFrame(frame);
            }
        }
#pragma warning disable CA1031 // A failing driver must end in a visible camera error, not in an unhandled thread exception.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Camera {Role} capture failed", _role);
            _errorCode = "camera.captureFailed";
            _state = CameraState.Error;
        }
        finally
        {
            _source.Dispose();
            if (_state != CameraState.Error)
            {
                _state = CameraState.Off;
            }
            _stopped.TrySetResult();
        }
    }

    private bool TryOpen()
    {
        var errorCode = "camera.openFailed";
        try
        {
            _source.Open();
            return true;
        }
        catch (CameraOpenException ex)
        {
            _logger.LogWarning("Camera {Role} could not be opened: {Reason}", _role, ex.Message);
            errorCode = ex.ErrorCode;
        }
#pragma warning disable CA1031 // Native driver errors surface as arbitrary exceptions; all mean "not openable".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "Camera {Role} could not be opened", _role);
        }
        _errorCode = errorCode;
        _state = CameraState.Error;
        return false;
    }
}

/// <summary>Measured frame rate over windows of about one second; written by the capture thread, read by anyone.</summary>
internal sealed class FrameRateMeter(CaptureClock clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);
    private long _windowStart;
    private int _count;
    private long _rateBits;

    public double Rate => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _rateBits));

    public void Tick()
    {
        if (_windowStart == 0)
        {
            _windowStart = clock.Timestamp();
            return;
        }
        _count++;
        var elapsed = clock.Elapsed(_windowStart);
        if (elapsed >= Window)
        {
            Interlocked.Exchange(ref _rateBits, BitConverter.DoubleToInt64Bits(_count / elapsed.TotalSeconds));
            _count = 0;
            _windowStart = clock.Timestamp();
        }
    }
}
