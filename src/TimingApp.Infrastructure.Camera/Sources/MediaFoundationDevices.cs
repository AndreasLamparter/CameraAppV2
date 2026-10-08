using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using TimingApp.Application.FinishRecording;
using Vortice.MediaFoundation;

namespace TimingApp.Infrastructure.Camera.Sources;

/// <summary>
/// Enumerates the Media Foundation video capture sources (<c>MFEnumDeviceSources</c>, via Vortice.MediaFoundation) in
/// the index order <see cref="MediaFoundationCameraSource"/> opens them. Capture modes come from the native media
/// types of each source; a source that cannot be activated (e.g. in use) keeps the modes last read for its path.
/// </summary>
internal sealed class MediaFoundationDevices(CameraOptions options, ILogger<MediaFoundationDevices> logger) : ICameraDeviceEnumerator
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<CameraMode>> _knownModes = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<EnumeratedDevice> List(bool includeModes = true)
    {
        if (!options.UsesMediaFoundation || !OperatingSystem.IsWindows())
        {
            return [];
        }
        try
        {
            return Enumerate(includeModes);
        }
#pragma warning disable CA1031 // Any Media Foundation failure means: no names, devices are listed by index only.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Media Foundation devices could not be enumerated");
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private List<EnumeratedDevice> Enumerate(bool includeModes)
    {
        MediaFactory.MFStartup(true).CheckError();
        try
        {
            using var attributes = MediaFactory.MFCreateAttributes(1);
            attributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap);
            using var devices = MediaFactory.MFEnumDeviceSources(attributes);
            return [.. devices.Select(device => Describe(device, includeModes))];
        }
        finally
        {
            MediaFactory.MFShutdown();
        }
    }

    [SupportedOSPlatform("windows")]
    private EnumeratedDevice Describe(IMFActivate device, bool includeModes)
    {
        var name = device.FriendlyName?.Trim() ?? string.Empty;
        var path = string.IsNullOrWhiteSpace(device.SymbolicLink) ? null : device.SymbolicLink.Trim();
        return new EnumeratedDevice(name, path) { Modes = includeModes ? Modes(device, path) : [] };
    }

    [SupportedOSPlatform("windows")]
    private IReadOnlyList<CameraMode> Modes(IMFActivate device, string? path)
    {
        try
        {
            var modes = DirectShowModes.Select(ReadModes(device), options.FourCc);
            if (path is not null && modes.Count > 0)
            {
                _knownModes[path] = modes;
            }
            return modes;
        }
#pragma warning disable CA1031 // A source in use cannot be activated; it stays selectable with its last known modes.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogDebug(ex, "Capture modes of Media Foundation device {Path} could not be read", path);
            return path is not null && _knownModes.TryGetValue(path, out var known) ? known : [];
        }
    }

    [SupportedOSPlatform("windows")]
    private static List<CameraMode> ReadModes(IMFActivate device)
    {
        using var source = device.ActivateObject<IMFMediaSource>();
        // Disposing the source reader shuts the media source down.
        using var reader = MediaFactory.MFCreateSourceReaderFromMediaSource(source, null);
        var modes = new List<CameraMode>();
        for (var index = 0; ; index++)
        {
            IMFMediaType type;
            try
            {
                type = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, index);
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
                return modes;
            }
            using (type)
            {
                var size = type.GetUInt64(MediaTypeAttributeKeys.FrameSize);
                modes.Add(new CameraMode(
                    (int)(size >> 32),
                    (int)(size & 0xFFFFFFFF),
                    MediaFoundationCameraSource.FrameRate(type.GetUInt64(MediaTypeAttributeKeys.FrameRate)),
                    MediaFoundationCameraSource.FourCc(type.GetGUID(MediaTypeAttributeKeys.Subtype))));
            }
        }
    }
}
