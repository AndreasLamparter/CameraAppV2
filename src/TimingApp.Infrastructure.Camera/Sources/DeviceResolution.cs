using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Infrastructure.Camera.Sources;

/// <summary>Finds the current index of the device selected in the camera settings.</summary>
internal static class DeviceResolution
{
    /// <summary>
    /// The index to open: the configured index when no device is selected by name; otherwise the device with the
    /// same path, else the only device with the same name. Null when the device is not connected or the name is
    /// ambiguous; another camera is never opened in its place.
    /// </summary>
    public static int? IndexOf(CameraSettings settings, IReadOnlyList<EnumeratedDevice> devices)
    {
        if (settings.Device is not { } device)
        {
            return settings.DeviceIndex;
        }
        if (device.Path is { } path)
        {
            var instance = InstancePath(path);
            for (var index = 0; index < devices.Count; index++)
            {
                if (devices[index].Path is { } candidate && string.Equals(InstancePath(candidate), instance, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }
        int? match = null;
        for (var index = 0; index < devices.Count; index++)
        {
            if (string.Equals(devices[index].Name, device.Name, StringComparison.Ordinal))
            {
                if (match is not null)
                {
                    return null;
                }
                match = index;
            }
        }
        return match;
    }

    /// <summary>
    /// The device instance part of a device interface path. DirectShow and Media Foundation report the same camera
    /// with different interface class suffixes (<c>#{guid}\global</c>), so a path saved with one backend still
    /// identifies the camera with the other.
    /// </summary>
    internal static string InstancePath(string path)
    {
        var suffix = path.IndexOf("#{", StringComparison.Ordinal);
        return suffix < 0 ? path : path[..suffix];
    }
}

/// <summary>Stands in for a selected camera that is not connected: opening fails with <c>camera.notFound</c>.</summary>
internal sealed class MissingCameraSource(CameraRole role, string deviceName) : ICameraSource
{
    public void Open() => throw new CameraOpenException($"{role} camera \"{deviceName}\" is not connected.", "camera.notFound");

    public bool Grab() => false;

    public bool Retrieve(OpenCvSharp.Mat frame) => false;

    public void Dispose()
    {
    }
}
