using System.Diagnostics;
using System.Globalization;
using OpenCvSharp;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Infrastructure.Camera.Sources;

/// <summary>Shared state of the camera simulation: whether a simulated rider is on the finish line.</summary>
public sealed class SimulationControl
{
    private volatile bool _occupied;

    public bool Occupied
    {
        get => _occupied;
        set => _occupied = value;
    }
}

/// <summary>Paces a source to its frame rate on the monotonic clock (no busy wait beyond the last millisecond).</summary>
internal sealed class FramePacer(double frameRate)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double _periodMs = 1000.0 / frameRate;
    private long _frame;

    public long FrameNumber => _frame;

    public void WaitForNextFrame()
    {
        var due = ++_frame * _periodMs;
        var remaining = due - _clock.Elapsed.TotalMilliseconds;
        if (remaining > 2)
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(remaining - 1.5));
        }
        while (_clock.Elapsed.TotalMilliseconds < due)
        {
            Thread.SpinWait(50);
        }
    }
}

/// <summary>
/// Synthetic camera with deterministic content: a static background and, while <see cref="SimulationControl.Occupied"/>
/// is set, a "rider" block on the finish line (finish camera) or moving through the image (front camera).
/// </summary>
internal sealed class SyntheticCameraSource(CameraRole role, CameraSettings settings, FinishLineSettings line, SimulationControl control, bool missing)
    : ICameraSource
{
    private readonly FramePacer _pacer = new(settings.FrameRate);
    private Mat? _background;

    public void Open()
    {
        if (missing)
        {
            throw new CameraOpenException($"Simulated {role} camera is not connected.");
        }
        _background = new Mat(settings.Height, settings.Width, MatType.CV_8UC3, new Scalar(90, 110, 100));
        Cv2.Rectangle(_background, new Rect(0, settings.Height * 2 / 3, settings.Width, settings.Height / 3), new Scalar(60, 70, 70), -1);
    }

    public bool Grab()
    {
        _pacer.WaitForNextFrame();
        return _background is not null;
    }

    public bool Retrieve(Mat frame)
    {
        if (_background is null)
        {
            return false;
        }
        _background.CopyTo(frame);
        var n = _pacer.FrameNumber;
        if (control.Occupied)
        {
            // Dark, varying colors: always clearly different from the background.
            var color = new Scalar(20 + (n * 7 % 40), 20, 40 + (n * 5 % 80));
            if (role == CameraRole.Finish)
            {
                Cv2.Rectangle(frame, RiderOnLine(), color, -1);
            }
            else
            {
                var x = (int)(n * 12 % settings.Width);
                Cv2.Rectangle(frame, new Rect(x, settings.Height / 4, settings.Width / 6, settings.Height / 2), color, -1);
            }
        }
        if (role == CameraRole.Front)
        {
            Cv2.PutText(frame, n.ToString(CultureInfo.InvariantCulture), new Point(20, 40), HersheyFonts.HersheySimplex, 1.0, Scalar.White, 2);
        }
        return true;
    }

    public void Dispose() => _background?.Dispose();

    private Rect RiderOnLine()
    {
        var (isColumn, start) = Capture.LineExtractor.InCameraImage(line, settings.Width, settings.Height);
        if (isColumn)
        {
            var x = Math.Clamp(start - 40, 0, settings.Width - 1);
            return new Rect(x, settings.Height / 4, Math.Min(80 + line.Width, settings.Width - x), settings.Height / 2);
        }
        var y = Math.Clamp(start - 40, 0, settings.Height - 1);
        return new Rect(settings.Width / 4, y, settings.Width / 2, Math.Min(80 + line.Width, settings.Height - y));
    }
}

/// <summary>Replays a recorded video file in a loop, paced to the configured frame rate.</summary>
internal sealed class VideoFileCameraSource(string path, CameraSettings settings) : ICameraSource
{
    private readonly FramePacer _pacer = new(settings.FrameRate);
    private VideoCapture? _capture;

    public void Open()
    {
        if (!File.Exists(path))
        {
            throw new CameraOpenException($"Video file {path} not found.");
        }
        _capture = new VideoCapture(path);
        if (!_capture.IsOpened())
        {
            _capture.Dispose();
            _capture = null;
            throw new CameraOpenException($"Video file {path} could not be opened.");
        }
    }

    public bool Grab()
    {
        if (_capture is null)
        {
            return false;
        }
        _pacer.WaitForNextFrame();
        if (_capture.Grab())
        {
            return true;
        }
        _capture.Set(VideoCaptureProperties.PosFrames, 0);
        return _capture.Grab();
    }

    public bool Retrieve(Mat frame) => _capture?.Retrieve(frame) == true;

    public void Dispose() => _capture?.Dispose();
}

/// <summary>Real USB cameras (Media Foundation or OpenCV), or simulated sources when <see cref="SimulationOptions.Enabled"/> is set.</summary>
internal sealed class CameraSourceFactory(CameraOptions options, SimulationControl control) : ICameraSourceFactory
{
    public bool IsSimulated => options.Simulation.Enabled;

    public ICameraSource Create(CameraRole role, CameraSettings settings, FinishLineSettings line)
    {
        var simulation = options.Simulation;
        if (!simulation.Enabled)
        {
            return options.UsesMediaFoundation && OperatingSystem.IsWindows()
                ? new MediaFoundationCameraSource(settings, options)
                : new UsbCameraSource(settings, options);
        }
        var file = role == CameraRole.Finish ? simulation.FinishVideoFile : simulation.FrontVideoFile;
        if (!string.IsNullOrWhiteSpace(file))
        {
            return new VideoFileCameraSource(file, settings);
        }
        var missing = role == CameraRole.Finish ? simulation.FinishCameraMissing : simulation.FrontCameraMissing;
        return new SyntheticCameraSource(role, settings, line, control, missing);
    }
}
