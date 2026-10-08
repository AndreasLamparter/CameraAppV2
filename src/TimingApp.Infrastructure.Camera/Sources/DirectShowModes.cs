using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using TimingApp.Application.FinishRecording;

namespace TimingApp.Infrastructure.Camera.Sources;

/// <summary>Capture modes a device offers, from the stream capabilities of its DirectShow capture pin.</summary>
internal static class DirectShowModes
{
    private static readonly Guid BaseFilter = new("56A86895-0AD4-11CE-B03A-0020AF0BA770");
    private static readonly Guid FormatVideoInfo = new("05589F80-C356-11CE-BF01-00AA0055595A");
    private static readonly Guid FormatVideoInfo2 = new("F72A76A0-EB0A-11D0-ACE4-0000C0CC16BA");

    // Offsets in VIDEOINFOHEADER / VIDEOINFOHEADER2 (strmif.h / dvdmedia.h).
    private const int AvgTimePerFrameOffset = 40;
    private const int BitmapHeaderOffset = 48;
    private const int BitmapHeader2Offset = 72;

    // Offset of MinFrameInterval in VIDEO_STREAM_CONFIG_CAPS.
    private const int MinFrameIntervalOffset = 104;
    private const double HundredNanosecondsPerSecond = 10_000_000;

    /// <summary>All modes of the first output pin with stream capabilities; empty when the driver reports none.</summary>
    [SupportedOSPlatform("windows")]
    public static List<CameraMode> Read(IMoniker moniker)
    {
        var iid = BaseFilter;
        moniker.BindToObject(null!, null!, ref iid, out var filterObject);
        var filter = (IBaseFilter)filterObject;
        try
        {
            if (filter.EnumPins(out var pins) != 0 || pins is null)
            {
                return [];
            }
            try
            {
                var batch = new IPin[1];
                while (pins.Next(1, batch, IntPtr.Zero) == 0)
                {
                    var pin = batch[0];
                    try
                    {
                        if (pin.QueryDirection(out var direction) == 0 && direction == PinDirection.Output && pin is IAMStreamConfig config)
                        {
                            return Capabilities(config);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(pin);
                    }
                }
                return [];
            }
            finally
            {
                Marshal.ReleaseComObject(pins);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(filter);
        }
    }

    /// <summary>
    /// The modes to offer: those in the configured pixel format when the camera has any (the capture requests it),
    /// otherwise all; one entry per size and format with its highest frame rate, largest first.
    /// </summary>
    public static IReadOnlyList<CameraMode> Select(IEnumerable<CameraMode> modes, string fourCc)
    {
        var all = modes.Where(m => m is { Width: > 0, Height: > 0, FrameRate: > 0 }).ToList();
        var preferred = all.Where(m => string.Equals(m.Format, fourCc, StringComparison.OrdinalIgnoreCase)).ToList();
        return [.. (preferred.Count > 0 ? preferred : all)
            .GroupBy(m => (m.Width, m.Height, m.Format))
            .Select(g => g.MaxBy(m => m.FrameRate)!)
            .OrderByDescending(m => m.Width * m.Height)
            .ThenByDescending(m => m.FrameRate)];
    }

    [SupportedOSPlatform("windows")]
    private static List<CameraMode> Capabilities(IAMStreamConfig config)
    {
        var modes = new List<CameraMode>();
        if (config.GetNumberOfCapabilities(out var count, out var size) != 0 || size <= 0)
        {
            return modes;
        }
        var caps = Marshal.AllocCoTaskMem(size);
        try
        {
            for (var index = 0; index < count; index++)
            {
                if (config.GetStreamCaps(index, out var mediaType, caps) != 0 || mediaType == IntPtr.Zero)
                {
                    continue;
                }
                try
                {
                    if (ToMode(Marshal.PtrToStructure<AmMediaType>(mediaType), caps, size) is { } mode)
                    {
                        modes.Add(mode);
                    }
                }
                finally
                {
                    FreeMediaType(mediaType);
                }
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(caps);
        }
        return modes;
    }

    private static CameraMode? ToMode(AmMediaType mediaType, IntPtr caps, int capsSize)
    {
        int headerOffset;
        if (mediaType.FormatType == FormatVideoInfo)
        {
            headerOffset = BitmapHeaderOffset;
        }
        else if (mediaType.FormatType == FormatVideoInfo2)
        {
            headerOffset = BitmapHeader2Offset;
        }
        else
        {
            return null;
        }
        if (mediaType.Format == IntPtr.Zero || mediaType.FormatSize < headerOffset + 12)
        {
            return null;
        }
        var width = Marshal.ReadInt32(mediaType.Format, headerOffset + 4);
        var height = Math.Abs(Marshal.ReadInt32(mediaType.Format, headerOffset + 8));
        // The fastest interval of the capabilities; the media type only holds the default frame rate.
        var interval = capsSize >= MinFrameIntervalOffset + 8 ? Marshal.ReadInt64(caps, MinFrameIntervalOffset) : 0;
        if (interval <= 0)
        {
            interval = Marshal.ReadInt64(mediaType.Format, AvgTimePerFrameOffset);
        }
        var frameRate = interval > 0 ? Math.Round(HundredNanosecondsPerSecond / interval, 2) : 0;
        return new CameraMode(width, height, frameRate, FourCc(mediaType.SubType));
    }

    /// <summary>FOURCC subtypes (MJPG, YUY2, NV12, ...) of DirectShow and Media Foundation carry the code in their first four bytes; RGB subtypes do not.</summary>
    internal static string FourCc(Guid subType)
    {
        var bytes = subType.ToByteArray().AsSpan(0, 4);
        foreach (var b in bytes)
        {
            if (b is < 0x20 or > 0x7E)
            {
                return "RGB";
            }
        }
        return System.Text.Encoding.ASCII.GetString(bytes).Trim();
    }

    private static void FreeMediaType(IntPtr pointer)
    {
        var mediaType = Marshal.PtrToStructure<AmMediaType>(pointer);
        if (mediaType.Format != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(mediaType.Format);
        }
        if (mediaType.Unknown != IntPtr.Zero)
        {
            Marshal.Release(mediaType.Unknown);
        }
        Marshal.FreeCoTaskMem(pointer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AmMediaType
    {
        public Guid MajorType;
        public Guid SubType;
        public int FixedSizeSamples;
        public int TemporalCompression;
        public int SampleSize;
        public Guid FormatType;
        public IntPtr Unknown;
        public int FormatSize;
        public IntPtr Format;
    }

    private enum PinDirection
    {
        Input,
        Output,
    }

    /// <summary>IBaseFilter with its IPersist and IMediaFilter slots; only <see cref="EnumPins"/> is called.</summary>
    [ComImport]
    [Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBaseFilter
    {
        [PreserveSig]
        int GetClassID(out Guid classId);

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Pause();

        [PreserveSig]
        int Run(long start);

        [PreserveSig]
        int GetState(int timeout, out int state);

        [PreserveSig]
        int SetSyncSource(IntPtr clock);

        [PreserveSig]
        int GetSyncSource(out IntPtr clock);

        [PreserveSig]
        int EnumPins(out IEnumPins? pins);
    }

    [ComImport]
    [Guid("56A86892-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumPins
    {
        [PreserveSig]
        int Next(int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IPin[] pins, IntPtr fetched);
    }

    /// <summary>IPin up to <see cref="QueryDirection"/>; the earlier slots are never called.</summary>
    [ComImport]
    [Guid("56A86891-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPin
    {
        [PreserveSig]
        int Connect(IntPtr receivePin, IntPtr mediaType);

        [PreserveSig]
        int ReceiveConnection(IntPtr connector, IntPtr mediaType);

        [PreserveSig]
        int Disconnect();

        [PreserveSig]
        int ConnectedTo(out IntPtr pin);

        [PreserveSig]
        int ConnectionMediaType(IntPtr mediaType);

        [PreserveSig]
        int QueryPinInfo(IntPtr info);

        [PreserveSig]
        int QueryDirection(out PinDirection direction);
    }

    [ComImport]
    [Guid("C6E13340-30AC-11D0-A18C-00A0C9118956")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAMStreamConfig
    {
        [PreserveSig]
        int SetFormat(IntPtr mediaType);

        [PreserveSig]
        int GetFormat(out IntPtr mediaType);

        [PreserveSig]
        int GetNumberOfCapabilities(out int count, out int size);

        [PreserveSig]
        int GetStreamCaps(int index, out IntPtr mediaType, IntPtr caps);
    }
}
