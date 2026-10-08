using Microsoft.Extensions.Logging;
using OpenCvSharp;
using TimingApp.Application.FinishRecording;
using TimingApp.Infrastructure.Camera.Preview;
using TimingApp.Infrastructure.Camera.Sources;

namespace TimingApp.Infrastructure.Camera.Capture;

/// <summary>
/// Entry point of the camera infrastructure: starts sessions (serialized, never opening a device that is still being
/// released), lists devices and serves the live previews of the current session.
/// </summary>
internal sealed class CameraSystem(
    ICameraSourceFactory sources,
    ICameraDeviceEnumerator deviceEnumerator,
    CaptureClock clock,
    CameraOptions options,
    ILogger<CameraSystem> logger) : ICameraSystem, ILivePreview, IDisposable
{
    private const int MaxProbedDevices = 10;
    private static readonly CameraMode[] SimulatedModes =
    [
        new(1920, 1080, 90, "MJPG"),
        new(1280, 720, 30, "MJPG"),
        new(640, 480, 30, "MJPG"),
    ];

    private static readonly EnumeratedDevice[] SimulatedDevices =
    [
        new("Simulation 0", "simulation:0") { Modes = SimulatedModes },
        new("Simulation 1", "simulation:1") { Modes = SimulatedModes },
    ];
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly PreviewWatchers _watchers = new();
    private volatile CameraSession? _current;

    public async Task<ICameraSession> StartAsync(CameraSessionOptions sessionOptions, CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current is { } previous)
            {
                await previous.DisposeAsync().ConfigureAwait(false);
                await WaitForReleaseAsync(previous, cancellationToken).ConfigureAwait(false);
            }
            var session = new CameraSession(sessionOptions, ResolveDevices(sessionOptions), sources, clock, options, _watchers, logger);
            session.Start();
            _current = session;
            return session;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task<IReadOnlyList<CameraDevice>> ListDevicesAsync(CancellationToken cancellationToken)
    {
        if (sources.IsSimulated)
        {
            return [.. SimulatedDevices.Select((_, index) => Describe(index, SimulatedDevices, inUse: false))];
        }
        var session = _current;
        var inUse = new HashSet<int>();
        if (session is not null && !session.Released.IsCompleted)
        {
            if (session.FinishDeviceIndex is { } finish)
            {
                inUse.Add(finish);
            }
            if (session.FrontDeviceIndex is { } front)
            {
                inUse.Add(front);
            }
        }
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => options.UsesMediaFoundation ? Listed(inUse) : Probe(inUse), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public IDisposable Watch(PreviewKind kind) => _watchers.Watch(kind);

    public byte[]? Latest(PreviewKind kind)
    {
        var session = _current;
        return session is null || session.Released.IsCompleted ? null : session.LatestPreview(kind);
    }

    public void Dispose() => _lifecycle.Dispose();

    private async Task WaitForReleaseAsync(CameraSession previous, CancellationToken cancellationToken)
    {
        try
        {
            await previous.Released.WaitAsync(ReleaseTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Previous camera session still holds a device after {Timeout}", ReleaseTimeout);
        }
    }

    /// <summary>The device indices to open, looked up when a camera is selected by name (only then the devices are enumerated).</summary>
    private DeviceIndices ResolveDevices(CameraSessionOptions sessionOptions)
    {
        var byName = sessionOptions.FinishCamera.Device is not null || sessionOptions.FrontCamera?.Device is not null;
        var devices = !byName ? [] : sources.IsSimulated ? SimulatedDevices : deviceEnumerator.List(includeModes: false);
        var finish = Resolve(CameraRole.Finish, sessionOptions.FinishCamera, devices);
        var front = sessionOptions.FrontCamera is { } frontCamera ? Resolve(CameraRole.Front, frontCamera, devices) : null;
        return new DeviceIndices(finish, front);
    }

    private int? Resolve(CameraRole role, Domain.FinishRecording.CameraSettings settings, IReadOnlyList<EnumeratedDevice> devices)
    {
        var index = DeviceResolution.IndexOf(settings, devices);
        if (settings.Device is { } device)
        {
            if (index is { } found)
            {
                logger.LogInformation("Camera {Role} {Device} found at index {Index}", role, device.Name, found);
            }
            else
            {
                logger.LogWarning("Camera {Role} {Device} ({Path}) is not connected or not unique", role, device.Name, device.Path);
            }
        }
        return index;
    }

    /// <summary>Media Foundation lists its capture sources; they are not opened.</summary>
    private List<CameraDevice> Listed(HashSet<int> inUse)
    {
        var devices = deviceEnumerator.List();
        return [.. devices.Select((_, index) => Describe(index, devices, inUse.Contains(index)))];
    }

    /// <summary>OpenCV has no device enumeration: indices are probed by opening them; indices in use are not touched.</summary>
    private List<CameraDevice> Probe(HashSet<int> inUse)
    {
        var result = new List<CameraDevice>();
        var devices = deviceEnumerator.List();
        var api = UsbCameraSource.ToApi(options.Backend);
        for (var index = 0; index < MaxProbedDevices; index++)
        {
            if (inUse.Contains(index))
            {
                result.Add(Describe(index, devices, inUse: true));
                continue;
            }
            try
            {
                using var capture = new VideoCapture(index, api);
                if (capture.IsOpened())
                {
                    result.Add(Describe(index, devices, inUse: false));
                }
            }
#pragma warning disable CA1031 // Probing an index that does not exist may throw anything from the native driver.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogDebug(ex, "Probing camera index {Index} failed", index);
            }
        }
        return result;
    }

    /// <summary>
    /// The device at <paramref name="index"/> with its driver name, path and capture modes; without a driver name it
    /// can only be selected by index and is labelled <c>USB {index}</c>.
    /// </summary>
    internal static CameraDevice Describe(int index, IReadOnlyList<EnumeratedDevice> devices, bool inUse)
    {
        var enumerated = index < devices.Count ? devices[index] : null;
        var named = !string.IsNullOrWhiteSpace(enumerated?.Name) ? enumerated : null;
        return new CameraDevice(index, named?.Name ?? $"USB {index}", inUse, named?.Name, named?.Path, enumerated?.Modes ?? []);
    }
}
