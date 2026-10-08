namespace TimingApp.Infrastructure.Camera;

/// <summary>Capture backend for USB/UVC cameras.</summary>
public enum CaptureBackend
{
    /// <summary>Media Foundation on Windows, V4L2 on Linux.</summary>
    Auto,

    /// <summary>OpenCV with DirectShow (fallback; cannot enforce the pixel format reliably).</summary>
    DShow,

    /// <summary>Media Foundation, read directly (Windows).</summary>
    Msmf,

    /// <summary>OpenCV with Video4Linux2.</summary>
    V4L2,
}

/// <summary>Static technical camera configuration (<c>TimingApp:Camera</c>).</summary>
public sealed class CameraOptions
{
    public const string Section = "TimingApp:Camera";

    public CaptureBackend Backend { get; set; } = CaptureBackend.Auto;

    /// <summary>Pixel format requested from the camera; MJPG is required for high frame rates over USB 2.</summary>
    public string FourCc { get; set; } = "MJPG";

    /// <summary>Path of the ffmpeg executable; a bare name is searched on PATH.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>A running camera without a frame for this long is reported as <c>camera.noFrames</c>.</summary>
    public TimeSpan NoFramesTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>JPEG quality of buffered front frames.</summary>
    public int JpegQuality { get; set; } = 85;

    /// <summary>Maximum width of the live preview images.</summary>
    public int PreviewMaxWidth { get; set; } = 960;

    /// <summary>Length of the running finish image in the live view.</summary>
    public TimeSpan LiveStripDuration { get; set; } = TimeSpan.FromSeconds(5);


    public SimulationOptions Simulation { get; set; } = new();

    /// <summary>Whether cameras are read through Media Foundation (<see cref="Sources.MediaFoundationCameraSource"/>).</summary>
    internal bool UsesMediaFoundation => OperatingSystem.IsWindows() && Backend is CaptureBackend.Auto or CaptureBackend.Msmf;
}

/// <summary>
/// Camera simulation (<c>TimingApp:Camera:Simulation</c>): synthetic frames, or replay of video files, through the
/// same <see cref="Sources.ICameraSource"/> port as real cameras.
/// </summary>
public sealed class SimulationOptions
{
    public bool Enabled { get; set; }

    /// <summary>Optional video file replayed as finish camera instead of synthetic frames.</summary>
    public string? FinishVideoFile { get; set; }

    /// <summary>Optional video file replayed as front camera instead of synthetic frames.</summary>
    public string? FrontVideoFile { get; set; }

    /// <summary>Simulates a front camera that cannot be opened.</summary>
    public bool FrontCameraMissing { get; set; }

    /// <summary>Simulates a finish camera that cannot be opened.</summary>
    public bool FinishCameraMissing { get; set; }
}
