using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenCvSharp;
using TimingApp.Domain.FinishRecording;
using Vortice.MediaFoundation;

namespace TimingApp.Infrastructure.Camera.Sources;

/// <summary>
/// USB/UVC camera read directly through Media Foundation (source reader, synchronous). The native capture format is
/// chosen explicitly: the configured pixel format (MJPG) in the configured size with the highest frame rate, otherwise
/// YUY2 or NV12 in that size. OpenCV only decodes the frames. The exposure is set on the device through
/// <c>IAMCameraControl</c> and read back; a camera that does not take it reports <c>camera.exposureNotApplied</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MediaFoundationCameraSource(CameraSettings settings, CameraOptions options) : ICameraSource
{
    public const string ModeNotSupported = "camera.modeNotSupported";
    public const string ExposureNotApplied = "camera.exposureNotApplied";

    private const int CameraControlExposure = 4;
    private const int FlagAuto = 1;
    private const int FlagManual = 2;

    private IMFMediaSource? _source;
    private IMFSourceReader? _reader;
    private bool _started;
    private string _format = string.Empty;
    private byte[] _data = [];
    private int _length;

    public string? Warning { get; private set; }

    /// <summary>The native format in use, e.g. <c>MJPG 960x720@90</c>.</summary>
    public string Format => _format;

    public void Open()
    {
        MediaFactory.MFStartup(true).CheckError();
        _started = true;
        using var attributes = MediaFactory.MFCreateAttributes(1);
        attributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap);
        using (var devices = MediaFactory.MFEnumDeviceSources(attributes))
        {
            _source = devices.ElementAtOrDefault(settings.DeviceIndex)?.ActivateObject<IMFMediaSource>()
                ?? throw new CameraOpenException($"USB camera {settings.DeviceIndex} could not be opened.");
        }
        Warning = ApplyExposure(_source);
        _reader = MediaFactory.MFCreateSourceReaderFromMediaSource(_source, null);
        using var type = ChooseNativeType(_reader) ?? throw new CameraOpenException(
            $"USB camera {settings.DeviceIndex} offers no {settings.Width}x{settings.Height} format.", ModeNotSupported);
        _reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, type);
    }

    public bool Grab()
    {
        if (_reader is null)
        {
            return false;
        }
        while (true)
        {
            using var sample = _reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out _);
            if ((flags & (SourceReaderFlag.Error | SourceReaderFlag.EndOfStream)) != 0)
            {
                return false;
            }
            if (sample is not null)
            {
                Copy(sample);
                return true;
            }
        }
    }

    public byte[]? CopyJpeg() => _length > 0 && _format.StartsWith("MJPG", StringComparison.Ordinal) ? _data.AsSpan(0, _length).ToArray() : null;

    public bool Retrieve(Mat frame)
    {
        if (_length == 0)
        {
            return false;
        }
        var (width, height) = (settings.Width, settings.Height);
        switch (_format[..4])
        {
            case "YUY2":
                using (var yuy2 = Mat.FromPixelData(height, width, MatType.CV_8UC2, _data))
                {
                    Cv2.CvtColor(yuy2, frame, ColorConversionCodes.YUV2BGR_YUY2);
                }
                return true;
            case "NV12":
                using (var nv12 = Mat.FromPixelData(height * 3 / 2, width, MatType.CV_8UC1, _data))
                {
                    Cv2.CvtColor(nv12, frame, ColorConversionCodes.YUV2BGR_NV12);
                }
                return true;
            default:
                using (var decoded = Cv2.ImDecode(_data.AsSpan(0, _length), ImreadModes.Color))
                {
                    if (decoded.Empty())
                    {
                        return false;
                    }
                    decoded.CopyTo(frame);
                }
                return true;
        }
    }

    public void Dispose()
    {
        if (_reader is not null)
        {
            // Disposing the source reader shuts the media source down.
            _reader.Dispose();
            _reader = null;
        }
        else
        {
            _source?.Shutdown();
        }
        _source?.Dispose();
        _source = null;
        if (_started)
        {
            MediaFactory.MFShutdown();
            _started = false;
        }
    }

    /// <summary>The native type in the configured size: preferred pixel format first, then YUY2, then NV12; highest frame rate.</summary>
    private IMFMediaType? ChooseNativeType(IMFSourceReader reader)
    {
        string[] formats = [options.FourCc.ToUpperInvariant(), "YUY2", "NV12"];
        IMFMediaType? best = null;
        var bestRank = (Format: int.MaxValue, Rate: 0.0);
        for (var index = 0; ; index++)
        {
            IMFMediaType type;
            try
            {
                type = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, index);
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
                break;
            }
            var size = type.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            var format = Array.IndexOf(formats, FourCc(type.GetGUID(MediaTypeAttributeKeys.Subtype)));
            var rate = FrameRate(type.GetUInt64(MediaTypeAttributeKeys.FrameRate));
            var fits = (int)(size >> 32) == settings.Width && (int)(size & 0xFFFFFFFF) == settings.Height && format >= 0;
            if (fits && (format < bestRank.Format || (format == bestRank.Format && rate > bestRank.Rate)))
            {
                best?.Dispose();
                best = type;
                bestRank = (format, rate);
                _format = $"{formats[format]} {settings.Width}x{settings.Height}@{rate:0.##}";
            }
            else
            {
                type.Dispose();
            }
        }
        return best;
    }

    private void Copy(IMFSample sample)
    {
        using var buffer = sample.ConvertToContiguousBuffer();
        buffer.Lock(out var pointer, out _, out var length);
        try
        {
            if (_data.Length < length)
            {
                _data = new byte[length];
            }
            Marshal.Copy(pointer, _data, 0, length);
            _length = length;
        }
        finally
        {
            buffer.Unlock();
        }
    }

    /// <summary>Sets the exposure (driver value, <c>null</c> = automatic) and reads it back.</summary>
    private string? ApplyExposure(IMFMediaSource source)
    {
        var unknown = Marshal.GetObjectForIUnknown(source.NativePointer);
        try
        {
            return unknown is IAMCameraControl control ? ApplyExposure(control) : ExposureNotApplied;
        }
        finally
        {
            Marshal.ReleaseComObject(unknown);
        }
    }

    private string? ApplyExposure(IAMCameraControl control)
    {
        if (control.GetRange(CameraControlExposure, out var min, out var max, out _, out var defaultValue, out _) != 0)
        {
            return ExposureNotApplied;
        }
        var wanted = settings.Exposure is { } value ? (int)Math.Round(value) : defaultValue;
        if (wanted < min || wanted > max ||
            control.Set(CameraControlExposure, wanted, settings.Exposure is null ? FlagAuto : FlagManual) != 0 ||
            control.Get(CameraControlExposure, out var actual, out var flags) != 0)
        {
            return ExposureNotApplied;
        }
        var applied = settings.Exposure is null ? (flags & FlagAuto) != 0 : actual == wanted && (flags & FlagManual) != 0;
        return applied ? null : ExposureNotApplied;
    }

    /// <summary>FOURCC subtypes (MJPG, YUY2, NV12, ...) carry the code in their first four bytes.</summary>
    internal static string FourCc(Guid subtype) => DirectShowModes.FourCc(subtype);

    /// <summary>Packed UINT64 ratio: numerator in the high, denominator in the low 32 bits.</summary>
    internal static double FrameRate(ulong packed)
    {
        var denominator = (uint)(packed & 0xFFFFFFFF);
        return denominator == 0 ? 0 : Math.Round((double)(uint)(packed >> 32) / denominator, 2);
    }

    /// <summary>UVC camera controls of the capture source; not part of Vortice.MediaFoundation.</summary>
    [ComImport]
    [Guid("C6E13370-30AC-11D0-A18C-00A0C9118956")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAMCameraControl
    {
        [PreserveSig] int GetRange(int property, out int min, out int max, out int step, out int defaultValue, out int flags);
        [PreserveSig] int Set(int property, int value, int flags);
        [PreserveSig] int Get(int property, out int value, out int flags);
    }
}
