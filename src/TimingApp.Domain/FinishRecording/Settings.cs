using TimingApp.Domain.SharedKernel;

namespace TimingApp.Domain.FinishRecording;

/// <summary>Operating state of the finish recording (FS1-01).</summary>
public enum OperatingMode
{
    Stopped,
    Preview,
    Recording,
}

/// <summary>
/// Rotation that turns the finish camera image upright, e.g. for a camera mounted turned by 90° so that its long side
/// runs along the finish line. Preview, finish line and finish image refer to the turned image.
/// </summary>
public enum ImageRotation
{
    None,
    Clockwise90,
    CounterClockwise90,
    Rotate180,
}

/// <summary>
/// A camera chosen by its driver name and, where the driver reports one, its unique device path. The device index is
/// looked up from it at every camera start.
/// </summary>
public sealed record CameraDeviceRef(string Name, string? Path)
{
    public const int MaxNameLength = 256;
    public const int MaxPathLength = 1024;

    /// <summary>
    /// Whether both refer to the same device: equal paths when both have one, otherwise equal names (a device chosen
    /// by name alone may resolve to the device with the path).
    /// </summary>
    public bool SameDeviceAs(CameraDeviceRef other) =>
        Path is not null && other.Path is not null
            ? string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase)
            : string.Equals(Name, other.Name, StringComparison.Ordinal);

    internal bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) && Name.Length <= MaxNameLength &&
        (Path is null || (!string.IsNullOrWhiteSpace(Path) && Path.Length <= MaxPathLength));
}

/// <summary>
/// Capture settings of one camera. <see cref="Device"/> selects the camera; without it (standard settings, backends
/// without device names) <see cref="DeviceIndex"/> does. <see cref="Exposure"/> is the driver value; <c>null</c> means
/// automatic exposure.
/// </summary>
public sealed record CameraSettings(int DeviceIndex, int Width, int Height, int FrameRate, double? Exposure, int OffsetMs, CameraDeviceRef? Device = null)
{
    public const int MaxDeviceIndex = 63;
    public const int MinSize = 160;
    public const int MaxSize = 4096;
    public const int MinFrameRate = 1;
    public const int MaxFrameRate = 240;
    public const int MaxOffsetMs = 1000;

    /// <summary>Correction added to every timestamp of this camera (FS1-32).</summary>
    public TimeSpan Offset => TimeSpan.FromMilliseconds(OffsetMs);

    /// <summary>Whether both settings select the same device; a device by name and one by index cannot be compared.</summary>
    public bool SameDeviceAs(CameraSettings other) => (Device, other.Device) switch
    {
        (null, null) => DeviceIndex == other.DeviceIndex,
        ({ } device, { } otherDevice) => device.SameDeviceAs(otherDevice),
        _ => false,
    };

    internal Result Validate(string camera)
    {
        if (DeviceIndex is < 0 or > MaxDeviceIndex)
        {
            return Error.Validation("settings.deviceInvalid", Params(camera, ("max", MaxDeviceIndex)));
        }
        if (Device is { IsValid: false })
        {
            return Error.Validation("settings.deviceNameInvalid", Params(camera, ("max", CameraDeviceRef.MaxNameLength)));
        }
        if (Width is < MinSize or > MaxSize || Height is < MinSize or > MaxSize)
        {
            return Error.Validation("settings.resolutionInvalid", Params(camera, ("min", MinSize), ("max", MaxSize)));
        }
        if (FrameRate is < MinFrameRate or > MaxFrameRate)
        {
            return Error.Validation("settings.frameRateInvalid", Params(camera, ("min", MinFrameRate), ("max", MaxFrameRate)));
        }
        if (Exposure is { } exposure && !double.IsFinite(exposure))
        {
            return Error.Validation("settings.exposureInvalid", Params(camera));
        }
        if (OffsetMs is < -MaxOffsetMs or > MaxOffsetMs)
        {
            return Error.Validation("settings.offsetInvalid", Params(camera, ("min", -MaxOffsetMs), ("max", MaxOffsetMs)));
        }
        return Result.Success();
    }

    private static Dictionary<string, object?> Params(string camera, params (string Key, object? Value)[] values)
    {
        var result = new Dictionary<string, object?> { ["camera"] = camera };
        foreach (var (key, value) in values)
        {
            result[key] = value;
        }
        return result;
    }
}

/// <summary>
/// Rotation of the finish camera image, position and width of the finish line and the time direction of the finish
/// image (FS1-11, FS1-12). The finish line is a pixel column of the turned image; <see cref="Position"/> counts from its
/// left edge.
/// </summary>
public sealed record FinishLineSettings(ImageRotation Rotation, int Position, int Width, bool ReverseTimeDirection)
{
    public const int MaxWidth = 64;

    /// <summary>Whether the turned image swaps width and height of the camera image.</summary>
    public bool SwapsSides => Rotation is ImageRotation.Clockwise90 or ImageRotation.CounterClockwise90;

    /// <summary>Width of the turned image, i.e. the range of <see cref="Position"/>.</summary>
    public int ImageWidth(CameraSettings finishCamera) => SwapsSides ? finishCamera.Height : finishCamera.Width;

    internal Result Validate(CameraSettings finishCamera)
    {
        if (!Enum.IsDefined(Rotation))
        {
            return Error.Validation("settings.rotationInvalid");
        }
        if (Width is < 1 or > MaxWidth)
        {
            return Error.Validation("settings.lineWidthInvalid", new Dictionary<string, object?> { ["max"] = MaxWidth });
        }
        var extent = ImageWidth(finishCamera);
        if (Position < 0 || Position + Width > extent)
        {
            return Error.Validation("settings.linePositionInvalid", new Dictionary<string, object?> { ["max"] = extent - Width });
        }
        return Result.Success();
    }
}

/// <summary>Finish event detection values (FS1-26).</summary>
public sealed record DetectionSettings(
    int PixelThreshold,
    double OccupancyThresholdPercent,
    TimeSpan PreRoll,
    TimeSpan PostRoll,
    TimeSpan MaxDuration,
    TimeSpan FrontPreRoll,
    TimeSpan FrontPostRoll)
{
    private static readonly TimeSpan MaxRoll = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinPostRoll = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MinMaxDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxMaxDuration = TimeSpan.FromSeconds(600);

    /// <summary>Occupancy as a fraction (0..1) at which the line counts as occupied.</summary>
    public double OccupancyThreshold => OccupancyThresholdPercent / 100.0;

    internal Result Validate()
    {
        if (PixelThreshold is < 1 or > 255)
        {
            return Error.Validation("settings.pixelThresholdInvalid", new Dictionary<string, object?> { ["min"] = 1, ["max"] = 255 });
        }
        if (!double.IsFinite(OccupancyThresholdPercent) || OccupancyThresholdPercent is < 0.1 or > 100)
        {
            return Error.Validation("settings.occupancyThresholdInvalid", new Dictionary<string, object?> { ["min"] = 0.1, ["max"] = 100 });
        }
        return CheckDuration("preRoll", PreRoll, TimeSpan.Zero, MaxRoll)
            ?? CheckDuration("postRoll", PostRoll, MinPostRoll, MaxRoll)
            ?? CheckDuration("maxDuration", MaxDuration, MinMaxDuration, MaxMaxDuration)
            ?? CheckDuration("frontPreRoll", FrontPreRoll, TimeSpan.Zero, MaxRoll)
            ?? CheckDuration("frontPostRoll", FrontPostRoll, TimeSpan.Zero, MaxRoll)
            ?? Result.Success();
    }

    private static Result? CheckDuration(string field, TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min || value > max
            ? Result.Failure(Error.Validation("settings.durationInvalid", new Dictionary<string, object?>
            {
                ["field"] = field,
                ["min"] = min.TotalSeconds,
                ["max"] = max.TotalSeconds,
            }))
            : null;
}

/// <summary>All runtime-editable settings of the finish recording (FS1-64). They apply from the next camera start (FS1-44).</summary>
public sealed record FinishRecordingSettings(
    string? MediaDirectory,
    CameraSettings FinishCamera,
    CameraSettings FrontCamera,
    bool FrontCameraEnabled,
    FinishLineSettings FinishLine,
    DetectionSettings Detection,
    int PassageOffsetMs = 0)
{
    public const int MaxMediaDirectoryLength = 240;
    public const int MaxPassageOffsetMs = 10_000;

    /// <summary>Correction added to every passage time reported by the timing system (FS2-18).</summary>
    public TimeSpan PassageOffset => TimeSpan.FromMilliseconds(PassageOffsetMs);

    /// <summary>Standard values; detection values from FS1-26.</summary>
    public static FinishRecordingSettings Default { get; } = new(
        MediaDirectory: null,
        FinishCamera: new CameraSettings(DeviceIndex: 0, Width: 1920, Height: 1080, FrameRate: 90, Exposure: null, OffsetMs: 0),
        FrontCamera: new CameraSettings(DeviceIndex: 1, Width: 1280, Height: 720, FrameRate: 30, Exposure: null, OffsetMs: 0),
        FrontCameraEnabled: true,
        FinishLine: new FinishLineSettings(ImageRotation.None, Position: 960, Width: 1, ReverseTimeDirection: false),
        Detection: new DetectionSettings(
            PixelThreshold: 25,
            OccupancyThresholdPercent: 2,
            PreRoll: TimeSpan.FromSeconds(0.5),
            PostRoll: TimeSpan.FromSeconds(1.0),
            MaxDuration: TimeSpan.FromSeconds(60),
            FrontPreRoll: TimeSpan.FromSeconds(2.0),
            FrontPostRoll: TimeSpan.FromSeconds(2.0)));

    public Result Validate()
    {
        if (MediaDirectory is not null &&
            (MediaDirectory.Length > MaxMediaDirectoryLength || !Path.IsPathFullyQualified(MediaDirectory) ||
             MediaDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0))
        {
            return Error.Validation("settings.mediaDirectoryInvalid", new Dictionary<string, object?> { ["max"] = MaxMediaDirectoryLength });
        }
        if (PassageOffsetMs is < -MaxPassageOffsetMs or > MaxPassageOffsetMs)
        {
            return Error.Validation("settings.passageOffsetInvalid", new Dictionary<string, object?> { ["min"] = -MaxPassageOffsetMs, ["max"] = MaxPassageOffsetMs });
        }
        var result = FinishCamera.Validate("finish");
        if (result.IsSuccess)
        {
            result = FrontCamera.Validate("front");
        }
        if (result.IsSuccess && FrontCameraEnabled && FrontCamera.SameDeviceAs(FinishCamera))
        {
            result = Error.Validation("settings.sameDevice");
        }
        if (result.IsSuccess)
        {
            result = FinishLine.Validate(FinishCamera);
        }
        return result.IsSuccess ? Detection.Validate() : result;
    }
}
