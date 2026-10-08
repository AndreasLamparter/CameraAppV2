using OpenCvSharp;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Infrastructure.Camera.Sources;

/// <summary>
/// USB/UVC camera opened through OpenCV <see cref="VideoCapture"/> by device index (DirectShow, V4L2). On Windows the
/// default is <see cref="MediaFoundationCameraSource"/>, which can enforce the pixel format.
/// </summary>
internal sealed class UsbCameraSource(CameraSettings settings, CameraOptions options) : ICameraSource
{
    private VideoCapture? _capture;

    public void Open()
    {
        var capture = new VideoCapture(settings.DeviceIndex, ToApi(options.Backend));
        if (!capture.IsOpened())
        {
            capture.Dispose();
            throw new CameraOpenException($"USB camera {settings.DeviceIndex} could not be opened.");
        }
        capture.Set(VideoCaptureProperties.FrameWidth, settings.Width);
        capture.Set(VideoCaptureProperties.FrameHeight, settings.Height);
        capture.Set(VideoCaptureProperties.Fps, settings.FrameRate);
        // After the size: DirectShow resets the pixel format when the size changes.
        if (options.FourCc is { Length: 4 } fourCc)
        {
            capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC(fourCc));
        }
        capture.Set(VideoCaptureProperties.BufferSize, 1);
        ApplyExposure(capture);
        _capture = capture;
    }

    public bool Grab() => _capture?.Grab() == true;

    public bool Retrieve(Mat frame) => _capture?.Retrieve(frame) == true;

    public void Dispose()
    {
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
    }

    internal static VideoCaptureAPIs ToApi(CaptureBackend backend) => backend switch
    {
        CaptureBackend.DShow => VideoCaptureAPIs.DSHOW,
        CaptureBackend.Msmf => VideoCaptureAPIs.MSMF,
        CaptureBackend.V4L2 => VideoCaptureAPIs.V4L2,
        _ when OperatingSystem.IsWindows() => VideoCaptureAPIs.DSHOW,
        _ when OperatingSystem.IsLinux() => VideoCaptureAPIs.V4L2,
        _ => VideoCaptureAPIs.ANY,
    };

    /// <summary>Exposure through OpenCV (DirectShow, V4L2): auto exposure is 0.75 (auto) / 0.25 (manual) with DirectShow, 3 / 1 with V4L2.</summary>
    private void ApplyExposure(VideoCapture capture)
    {
        var backend = capture.GetBackendName() ?? string.Empty;
        var windows = backend.Contains("DSHOW", StringComparison.OrdinalIgnoreCase);
        if (settings.Exposure is { } exposure)
        {
            capture.Set(VideoCaptureProperties.AutoExposure, windows ? 0.25 : 1);
            capture.Set(VideoCaptureProperties.Exposure, exposure);
        }
        else
        {
            capture.Set(VideoCaptureProperties.AutoExposure, windows ? 0.75 : 3);
        }
    }
}
