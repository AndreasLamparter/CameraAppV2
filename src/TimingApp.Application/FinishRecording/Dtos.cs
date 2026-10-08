using TimingApp.Domain.FinishRecording;

namespace TimingApp.Application.FinishRecording;

/// <summary>State of the finish line shown in the live view: free, occupied, or a running event that will be saved.</summary>
public enum LineState
{
    Free,
    Occupied,
    Recording,
}

public sealed record CameraStatus(CameraState State, string? ErrorCode, double MeasuredFrameRate, int ConfiguredFrameRate, string? WarningCode = null);

/// <summary>Finish line position as fractions of the width of the turned finish camera image (a vertical line), for the overlay in the live view.</summary>
public sealed record LineOverlay(double Position, double Width);

public sealed record LineStatus(
    LineState State,
    double OccupancyPercent,
    double? LineRate,
    bool LineRateWarning,
    bool ManualTrigger,
    bool BackgroundLearning,
    bool EventRunning,
    DateTimeOffset? EventStartedAt,
    int DetectedEvents);

/// <summary>Most recent problem while saving a recording (e.g. <c>video.ffmpegMissing</c>, FS1-43).</summary>
public sealed record SaveProblem(string Code, string? RecordingId, DateTimeOffset At);

/// <summary>Status for the live view and for external programs (FS1-50, FS1-60).</summary>
public sealed record FinishRecordingStatus(
    OperatingMode Mode,
    CameraStatus FinishCamera,
    CameraStatus FrontCamera,
    LineStatus Line,
    LineOverlay? Overlay,
    int PendingSaves,
    SaveProblem? LastProblem,
    DateTimeOffset ServerTime);

/// <summary>Result of a video of a recording; <see cref="ErrorCode"/> is set when the video could not be created.</summary>
public sealed record VideoInfo(bool Available, string? ErrorCode);

/// <summary>Front video of a recording with the timestamp (Unix microseconds) of every frame.</summary>
public sealed record FrontVideoInfo(bool Available, string? ErrorCode, double FrameRate, IReadOnlyList<long> FrameTimestamps);

/// <summary>
/// Metadata of a complete recording (FS1-40), written last. Timestamps are Unix microseconds on the shared time base
/// including the camera offsets; column i of <see cref="ColumnTimestamps"/> is column i in time order.
/// </summary>
public sealed record RecordingMetadata(
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    DateTimeOffset EventStartedAt,
    DateTimeOffset EventEndedAt,
    FinishEventEnd EndReason,
    double LineRate,
    bool ReverseTimeDirection,
    int ImageWidth,
    int ImageHeight,
    int TimelineHeight,
    IReadOnlyList<long> ColumnTimestamps,
    VideoInfo FinishVideo,
    FrontVideoInfo? FrontVideo,
    int FinishOffsetMs,
    int FrontOffsetMs);

/// <summary>Entry of the recording list (FS1-61).</summary>
public sealed record RecordingSummary(
    string Id,
    DateTimeOffset StartedAt,
    double DurationSeconds,
    double LineRate,
    bool HasFrontVideo,
    bool HasVideoError,
    long SizeBytes);

/// <summary>Capture settings of one camera; <see cref="DeviceName"/> (and <see cref="DevicePath"/>) select the device, otherwise <see cref="DeviceIndex"/>.</summary>
public sealed record CameraSettingsDto(
    int DeviceIndex,
    int Width,
    int Height,
    int FrameRate,
    double? Exposure,
    int OffsetMs,
    string? DeviceName = null,
    string? DevicePath = null);

public sealed record FinishLineDto(ImageRotation Rotation, int Position, int Width, bool ReverseTimeDirection);

/// <summary>Detection values (FS1-26); durations in seconds.</summary>
public sealed record DetectionDto(
    int PixelThreshold,
    double OccupancyThresholdPercent,
    double PreRollSeconds,
    double PostRollSeconds,
    double MaxDurationSeconds,
    double FrontPreRollSeconds,
    double FrontPostRollSeconds);

public sealed record SettingsDto(
    string? MediaDirectory,
    CameraSettingsDto FinishCamera,
    CameraSettingsDto FrontCamera,
    bool FrontCameraEnabled,
    FinishLineDto FinishLine,
    DetectionDto Detection);

/// <summary>Settings with the effective media directory and whether a change only applies after restarting the cameras (FS1-44).</summary>
public sealed record SettingsResponse(SettingsDto Settings, string EffectiveMediaDirectory, bool AppliesOnNextStart);

/// <summary>Mapping between transport DTOs and domain settings.</summary>
public static class SettingsMapping
{
    public static SettingsDto ToDto(this FinishRecordingSettings s) => new(
        s.MediaDirectory,
        ToDto(s.FinishCamera),
        ToDto(s.FrontCamera),
        s.FrontCameraEnabled,
        new FinishLineDto(s.FinishLine.Rotation, s.FinishLine.Position, s.FinishLine.Width, s.FinishLine.ReverseTimeDirection),
        new DetectionDto(
            s.Detection.PixelThreshold,
            s.Detection.OccupancyThresholdPercent,
            s.Detection.PreRoll.TotalSeconds,
            s.Detection.PostRoll.TotalSeconds,
            s.Detection.MaxDuration.TotalSeconds,
            s.Detection.FrontPreRoll.TotalSeconds,
            s.Detection.FrontPostRoll.TotalSeconds));

    public static FinishRecordingSettings ToDomain(this SettingsDto d) => new(
        string.IsNullOrWhiteSpace(d.MediaDirectory) ? null : d.MediaDirectory.Trim(),
        ToDomain(d.FinishCamera),
        ToDomain(d.FrontCamera),
        d.FrontCameraEnabled,
        new FinishLineSettings(d.FinishLine.Rotation, d.FinishLine.Position, d.FinishLine.Width, d.FinishLine.ReverseTimeDirection),
        new DetectionSettings(
            d.Detection.PixelThreshold,
            d.Detection.OccupancyThresholdPercent,
            Seconds(d.Detection.PreRollSeconds),
            Seconds(d.Detection.PostRollSeconds),
            Seconds(d.Detection.MaxDurationSeconds),
            Seconds(d.Detection.FrontPreRollSeconds),
            Seconds(d.Detection.FrontPostRollSeconds)));

    private static CameraSettingsDto ToDto(CameraSettings c) =>
        new(c.DeviceIndex, c.Width, c.Height, c.FrameRate, c.Exposure, c.OffsetMs, c.Device?.Name, c.Device?.Path);

    private static CameraSettings ToDomain(CameraSettingsDto c) =>
        new(c.DeviceIndex, c.Width, c.Height, c.FrameRate, c.Exposure, c.OffsetMs, DeviceRef(c.DeviceName, c.DevicePath));

    /// <summary>No name means selection by index; the path alone does not identify a device for the operator.</summary>
    private static CameraDeviceRef? DeviceRef(string? name, string? path) =>
        string.IsNullOrWhiteSpace(name) ? null : new CameraDeviceRef(name.Trim(), string.IsNullOrWhiteSpace(path) ? null : path.Trim());

    /// <summary>Out-of-range values become an invalid duration that validation rejects.</summary>
    private static TimeSpan Seconds(double value) =>
        double.IsFinite(value) && Math.Abs(value) < 1e6 ? TimeSpan.FromSeconds(value) : TimeSpan.MinValue;
}

/// <summary>Unix microseconds: the timestamp format of recording metadata and the API.</summary>
public static class UnixMicroseconds
{
    public static long From(DateTimeOffset timestamp) => (timestamp.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;

    public static DateTimeOffset ToTimestamp(long microseconds) => DateTimeOffset.UnixEpoch.AddTicks(microseconds * 10);
}
