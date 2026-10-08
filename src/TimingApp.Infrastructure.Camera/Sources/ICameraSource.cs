using OpenCvSharp;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Infrastructure.Camera.Sources;

/// <summary>
/// A frame source used by one capture thread: a USB camera, a synthetic simulator or a video file replay. Not
/// thread-safe; <see cref="Grab"/> blocks until the next frame is acquired.
/// </summary>
public interface ICameraSource : IDisposable
{
    /// <summary>Opens the device; throws <see cref="CameraOpenException"/> when that is not possible.</summary>
    void Open();

    /// <summary>Acquires the next frame (blocking). The capture timestamp is taken right after it returns.</summary>
    bool Grab();

    /// <summary>Decodes the grabbed frame into <paramref name="frame"/> (BGR).</summary>
    bool Retrieve(Mat frame);

    /// <summary>Stable code of a problem found while opening that does not stop the capture (e.g. <c>camera.exposureNotApplied</c>).</summary>
    string? Warning => null;

    /// <summary>
    /// A copy of the grabbed frame as delivered by the camera when that is a JPEG (MJPG), otherwise null. Lets the
    /// pipeline decode on other threads, or keep the JPEG without decoding it.
    /// </summary>
    byte[]? CopyJpeg() => null;
}

/// <summary>Creates the source of a camera from its settings (real or simulated, depending on the configuration).</summary>
public interface ICameraSourceFactory
{
    ICameraSource Create(CameraRole role, CameraSettings settings, FinishLineSettings line);

    /// <summary>True when sources are simulated; device enumeration then reports simulated devices.</summary>
    bool IsSimulated { get; }
}

/// <summary>A camera could not be opened; <see cref="ErrorCode"/> is the stable code reported in the camera health.</summary>
public sealed class CameraOpenException(string message, string errorCode = "camera.openFailed") : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
