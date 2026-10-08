using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;
using TimingApp.Infrastructure.Camera.Preview;
using TimingApp.Infrastructure.Camera.Rendering;
using TimingApp.Infrastructure.Camera.Sources;

namespace TimingApp.Infrastructure.Camera.Capture;

/// <summary>Device index to open per camera; null when the camera selected by name is not connected.</summary>
internal sealed record DeviceIndices(int? Finish, int? Front);

/// <summary>
/// A running capture session: one worker per camera, the column channel to the application, the front frame
/// buffer and the live previews. Data leaves the capture threads only through the parallel finish decoder (camera
/// JPEGs), the column channel, the bounded compression queue (front frames that are not JPEG) and immutable
/// references (columns, camera JPEGs).
/// </summary>
internal sealed class CameraSession : ICameraSession
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    // Written by the finish capture thread or, for camera JPEGs, by the decoder in capture order under its lock.
    private readonly Channel<LineColumn> _columns = Channel.CreateUnbounded<LineColumn>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ParallelFinishDecoder _decoder;
    private readonly CameraWorker _finish;
    private readonly CameraWorker? _front;
    private readonly FrontFrameBuffer? _frontFrames;
    private readonly Channel<(Mat Frame, DateTimeOffset Timestamp)>? _compression;
    private readonly Task _compressor = Task.CompletedTask;
    private readonly FramePreview _finishPreview;
    private readonly LiveStrip _strip;
    private readonly int _jpegQuality;
    private readonly ILogger _logger;
    private int _disposed;

    public CameraSession(
        CameraSessionOptions options,
        DeviceIndices devices,
        ICameraSourceFactory sources,
        CaptureClock clock,
        CameraOptions cameraOptions,
        PreviewWatchers watchers,
        ILogger logger)
    {
        _logger = logger;
        _jpegQuality = cameraOptions.JpegQuality;
        _finishPreview = new FramePreview(watchers, cameraOptions.PreviewMaxWidth, cameraOptions.JpegQuality, options.FinishLine.Rotation);
        _strip = new LiveStrip(
            cameraOptions.LiveStripDuration,
            options.FinishCamera.FrameRate,
            options.FinishLine.ReverseTimeDirection,
            cameraOptions.JpegQuality,
            clock.LocalTimeZone);
        FinishDeviceIndex = devices.Finish;
        _decoder = new ParallelFinishDecoder(ParallelFinishDecoder.DefaultWorkers, options.FinishLine, Emit, _finishPreview, logger);

        _finish = new CameraWorker(
            CameraRole.Finish,
            CreateSource(sources, CameraRole.Finish, options.FinishCamera, devices.Finish, options.FinishLine),
            clock,
            options.FinishCamera.Offset,
            new FinishSink(this, options.FinishLine),
            cameraOptions.NoFramesTimeout,
            logger);

        if (options.FrontCamera is { } front)
        {
            FrontDeviceIndex = devices.Front;
            _frontFrames = new FrontFrameBuffer(options.FrontRetention);
            _compression = Channel.CreateBounded<(Mat, DateTimeOffset)>(new BoundedChannelOptions(8) { SingleReader = true, SingleWriter = true });
            _front = new CameraWorker(
                CameraRole.Front,
                CreateSource(sources, CameraRole.Front, front, devices.Front, options.FinishLine),
                clock,
                front.Offset,
                new FrontSink(_frontFrames, _compression.Writer, logger),
                cameraOptions.NoFramesTimeout,
                logger);
            _compressor = Task.Run(CompressAsync);
        }
    }

    /// <summary>Index of the opened finish device; null when the selected device was not found.</summary>
    public int? FinishDeviceIndex { get; }

    public int? FrontDeviceIndex { get; }

    public ChannelReader<LineColumn> Columns => _columns.Reader;

    private static ICameraSource CreateSource(ICameraSourceFactory sources, CameraRole role, CameraSettings settings, int? index, FinishLineSettings line) =>
        index is { } found
            ? sources.Create(role, settings with { DeviceIndex = found }, line)
            : new MissingCameraSource(role, settings.Device?.Name ?? $"USB {settings.DeviceIndex}");

    public IFrontFrameBuffer? FrontFrames => _frontFrames;

    /// <summary>Completes when both capture threads have released their devices.</summary>
    public Task Released => Task.WhenAll(_finish.Stopped, _front?.Stopped ?? Task.CompletedTask);

    public void Start()
    {
        _finish.Start();
        _front?.Start();
    }

    public CameraHealth Health(CameraRole role) => role switch
    {
        CameraRole.Finish => _finish.Health(),
        _ => _front?.Health() ?? new CameraHealth(CameraState.Disabled, null, 0),
    };

    public byte[]? LatestPreview(PreviewKind kind) => kind switch
    {
        PreviewKind.FinishCamera => _finishPreview.LatestJpeg(),
        PreviewKind.FrontCamera => _frontFrames?.Latest?.Jpeg,
        _ => _strip.LatestJpeg(),
    };

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        _finish.RequestStop();
        _front?.RequestStop();
        try
        {
            await Released.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A driver hanging in Grab: the thread releases the device when it returns; the next start waits for it.
            _logger.LogWarning("A camera did not stop within {Timeout}", StopTimeout);
        }
        await _decoder.CompleteAsync().ConfigureAwait(false);
        _columns.Writer.TryComplete();
        _compression?.Writer.TryComplete();
        await _compressor.ConfigureAwait(false);
        _frontFrames?.Complete();
        _finishPreview.Dispose();
    }

    private async Task CompressAsync()
    {
        await foreach (var (frame, timestamp) in _compression!.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            using (frame)
            {
                var jpeg = ColumnImage.EncodeJpeg(frame, _jpegQuality);
                if (jpeg.Length > 0)
                {
                    _frontFrames!.Add(new EncodedFrame(timestamp, jpeg));
                }
            }
        }
    }

    /// <summary>A finish column in capture order: to the application and the running finish image.</summary>
    private void Emit(LineColumn column)
    {
        _columns.Writer.TryWrite(column);
        _strip.Add(column);
    }

    /// <summary>
    /// Finish camera frames: camera JPEGs go to the parallel decoder; other frames are decoded here, the column is
    /// extracted (cheap) and a preview copy offered when watched.
    /// </summary>
    private sealed class FinishSink(CameraSession session, FinishLineSettings line) : IFrameSink
    {
        public void OnFrame(CapturedFrame frame)
        {
            if (frame.CopyJpeg() is { } jpeg)
            {
                session._decoder.Post(jpeg, frame.Timestamp);
                return;
            }
            if (frame.Decode() is not { } decoded)
            {
                return;
            }
            session.Emit(new LineColumn(frame.Timestamp, LineExtractor.Extract(decoded, line)));
            session._finishPreview.Offer(decoded);
        }
    }

    /// <summary>
    /// Front camera frames: a camera JPEG is buffered as it is (no decoding, no re-encoding); other frames are copied
    /// into the bounded compression queue, dropped (and counted) when it is full.
    /// </summary>
    private sealed class FrontSink(FrontFrameBuffer buffer, ChannelWriter<(Mat, DateTimeOffset)> queue, ILogger logger) : IFrameSink
    {
        private long _dropped;

        public void OnFrame(CapturedFrame frame)
        {
            if (frame.CopyJpeg() is { } jpeg)
            {
                buffer.Add(new EncodedFrame(frame.Timestamp, jpeg));
                return;
            }
            if (frame.Decode() is not { } decoded)
            {
                return;
            }
            var copy = decoded.Clone();
            var timestamp = frame.Timestamp;
            if (queue.TryWrite((copy, timestamp)))
            {
                return;
            }
            copy.Dispose();
            if (++_dropped % 30 == 1)
            {
                logger.LogWarning("Front frame compression cannot keep up; {Dropped} frames dropped", _dropped);
            }
        }
    }
}
