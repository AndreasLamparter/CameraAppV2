using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Extensions.Logging;
using TimingApp.Application.FinishRecording;

namespace TimingApp.Infrastructure.Camera.Sources;

/// <summary>
/// A video capture device as reported by the driver: friendly name, the unique device path when reported, and the
/// capture modes it offers (empty when unknown).
/// </summary>
internal sealed record EnumeratedDevice(string Name, string? Path)
{
    public IReadOnlyList<CameraMode> Modes { get; init; } = [];
}

/// <summary>The video capture devices in the index order the capture backend opens them.</summary>
internal interface ICameraDeviceEnumerator
{
    /// <summary>
    /// Devices by index; empty when the backend has no device enumeration. Reading the capture modes activates the
    /// devices, so callers that only need names and paths skip it.
    /// </summary>
    IReadOnlyList<EnumeratedDevice> List(bool includeModes = true);
}

/// <summary>
/// Enumerates the DirectShow video input category for the <see cref="CaptureBackend.DShow"/> backend. OpenCV's
/// DirectShow backend opens devices in this order, so the position in the list is the device index.
/// Capture modes come from the stream capabilities of each device; a device whose modes cannot be read keeps its name.
/// </summary>
internal sealed class DirectShowDevices(CameraOptions options, ILogger<DirectShowDevices> logger) : ICameraDeviceEnumerator
{
    private static readonly Guid SystemDeviceEnum = new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86");
    private static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11D0-BD3B-00A0C911CE86");
    private static readonly Guid PropertyBag = new("55272A00-42CB-11CE-8135-00AA004BB851");

    public IReadOnlyList<EnumeratedDevice> List(bool includeModes = true)
    {
        if (!OperatingSystem.IsWindows() || options.Backend != CaptureBackend.DShow)
        {
            return [];
        }
        try
        {
            return Enumerate(includeModes);
        }
#pragma warning disable CA1031 // Any COM failure means: no names, devices are listed by index only.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "DirectShow devices could not be enumerated");
            return [];
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private List<EnumeratedDevice> Enumerate(bool includeModes)
    {
        var devices = new List<EnumeratedDevice>();
        var type = Type.GetTypeFromCLSID(SystemDeviceEnum, throwOnError: true)!;
        var devEnum = (ICreateDevEnum)Activator.CreateInstance(type)!;
        try
        {
            var category = VideoInputDeviceCategory;
            // S_FALSE (1) and a null enumerator mean: no device in this category.
            if (devEnum.CreateClassEnumerator(ref category, out var monikers, 0) != 0 || monikers is null)
            {
                return devices;
            }
            try
            {
                var batch = new IMoniker[1];
                while (monikers.Next(1, batch, IntPtr.Zero) == 0)
                {
                    try
                    {
                        var device = Describe(batch[0]);
                        devices.Add(includeModes ? device with { Modes = Modes(batch[0]) } : device);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(batch[0]);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(monikers);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(devEnum);
        }
        return devices;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static EnumeratedDevice Describe(IMoniker moniker)
    {
        var iid = PropertyBag;
        moniker.BindToStorage(null!, null!, ref iid, out var storage);
        var bag = (IPropertyBag)storage;
        try
        {
            return new EnumeratedDevice(Read(bag, "FriendlyName") ?? string.Empty, Read(bag, "DevicePath"));
        }
        finally
        {
            Marshal.ReleaseComObject(bag);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private IReadOnlyList<CameraMode> Modes(IMoniker moniker)
    {
        try
        {
            return DirectShowModes.Select(DirectShowModes.Read(moniker), options.FourCc);
        }
#pragma warning disable CA1031 // Drivers may refuse to create the filter (e.g. device busy); the device stays selectable.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogDebug(ex, "Capture modes of a DirectShow device could not be read");
            return [];
        }
    }

    /// <summary>A string property of the device; null when the driver does not report it (e.g. no DevicePath).</summary>
    private static string? Read(IPropertyBag bag, string property) =>
        bag.Read(property, out var value, IntPtr.Zero) == 0 && value is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid clsidDeviceClass, out IEnumMoniker? enumMoniker, int flags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string propertyName, out object? value, IntPtr errorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string propertyName, ref object value);
    }
}
