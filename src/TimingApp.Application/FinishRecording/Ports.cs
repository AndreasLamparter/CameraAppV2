using System.Threading.Channels;
using TimingApp.Domain.FinishRecording;
using TimingApp.Domain.SharedKernel;

namespace TimingApp.Application.FinishRecording;

/// <summary>
/// One column of the finish image: the finish-line pixels of one finish camera frame as interleaved BGR bytes, with
/// the capture timestamp on the shared time base including the camera offset (FS1-10, FS1-31, FS1-32). Immutable by
/// convention: the pixel array is never modified after creation.
/// </summary>
public sealed record LineColumn(DateTimeOffset Timestamp, byte[] Bgr);

/// <summary>A compressed front camera frame (JPEG) with its timestamp on the shared time base including the offset.</summary>
public sealed record EncodedFrame(DateTimeOffset Timestamp, byte[] Jpeg);

public enum CameraRole
{
    Finish,
    Front,
}

public enum CameraState
{
    Off,
    Disabled,
    Starting,
    Running,
    Error,
}

/// <summary>
/// Health of one camera: state, stable error code (e.g. <c>camera.openFailed</c>, <c>camera.noFrames</c>), measured
/// frame rate and the code of a problem that does not stop the capture (e.g. <c>camera.exposureNotApplied</c>).
/// </summary>
public sealed record CameraHealth(CameraState State, string? ErrorCode, double MeasuredFrameRate)
{
    public string? WarningCode { get; init; }

    public static CameraHealth Off { get; } = new(CameraState.Off, null, 0);
}

/// <summary>A capture mode offered by a camera: size, highest frame rate and pixel format (e.g. <c>MJPG</c>).</summary>
public sealed record CameraMode(int Width, int Height, double FrameRate, string Format);

/// <summary>
/// A local camera device that can be selected in the settings. <see cref="Name"/> is the display label;
/// <see cref="DeviceName"/> and <see cref="DevicePath"/> identify the device and are null when the backend reports no
/// names (selection by index only). <see cref="Modes"/> is empty when the backend cannot list them.
/// </summary>
public sealed record CameraDevice(int Index, string Name, bool InUse, string? DeviceName, string? DevicePath, IReadOnlyList<CameraMode> Modes);

/// <summary>What a camera session captures; the front camera is null when disabled.</summary>
public sealed record CameraSessionOptions(CameraSettings FinishCamera, FinishLineSettings FinishLine, CameraSettings? FrontCamera, TimeSpan FrontRetention);

/// <summary>Camera hardware (or its simulation) behind a port: starts capture sessions and lists devices.</summary>
public interface ICameraSystem
{
    /// <summary>
    /// Opens the cameras on their capture threads and returns immediately; open failures are reported per camera
    /// through <see cref="ICameraSession.Health"/>. Waits until a previous session has released its devices.
    /// </summary>
    Task<ICameraSession> StartAsync(CameraSessionOptions options, CancellationToken cancellationToken);

    Task<IReadOnlyList<CameraDevice>> ListDevicesAsync(CancellationToken cancellationToken);
}

/// <summary>A running capture session. Disposing stops the cameras and completes <see cref="Columns"/>.</summary>
public interface ICameraSession : IAsyncDisposable
{
    /// <summary>Finish-line columns in capture order; single reader.</summary>
    ChannelReader<LineColumn> Columns { get; }

    /// <summary>Buffered front camera frames; null when the front camera is disabled.</summary>
    IFrontFrameBuffer? FrontFrames { get; }

    CameraHealth Health(CameraRole role);
}

/// <summary>Time-bounded buffer of front camera frames. Stays readable after the session has stopped.</summary>
public interface IFrontFrameBuffer
{
    /// <summary>
    /// Frames covering [from, to] in order: those within the range plus the last frame at or before
    /// <paramref name="from"/> and the first frame at or after <paramref name="to"/>.
    /// </summary>
    IReadOnlyList<EncodedFrame> Snapshot(DateTimeOffset from, DateTimeOffset to);

    /// <summary>Completes when a frame at or after <paramref name="timestamp"/> is buffered, the session has stopped, or the timeout elapsed.</summary>
    Task WaitForAsync(DateTimeOffset timestamp, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Size of a rendered finish image; the timeline strip lies below the columns (FS1-13).</summary>
public sealed record FinishImageInfo(int Width, int Height, int TimelineHeight);

/// <summary>Renders the finish image with timeline from the columns of a recording (FS1-12, FS1-13).</summary>
public interface IFinishImageRenderer
{
    Task<FinishImageInfo> RenderAsync(IReadOnlyList<LineColumn> columns, bool reverseTimeDirection, string path, CancellationToken cancellationToken);
}

/// <summary>Video encoding (FS1-40). Failures are expected (<c>video.ffmpegMissing</c>, <c>video.encodingFailed</c>) and returned, not thrown.</summary>
public interface IVideoEncoder
{
    /// <summary>The finish image as a scrolling video, one column per frame at the line rate.</summary>
    Task<Result> EncodeFinishVideoAsync(string imagePath, FinishImageInfo image, double lineRate, bool reverseTimeDirection, string outputPath, CancellationToken cancellationToken);

    /// <summary>The front frames as a video with a constant frame rate; frame i is shown at i / frameRate.</summary>
    Task<Result> EncodeFrontVideoAsync(IReadOnlyList<EncodedFrame> frames, double frameRate, string outputPath, CancellationToken cancellationToken);
}

/// <summary>Files of a recording that is being written.</summary>
public sealed record RecordingTarget(RecordingId Id, string Directory, string FinishImagePath, string FinishVideoPath, string FrontVideoPath);

/// <summary>Downloadable files of a recording.</summary>
public enum RecordingFile
{
    FinishImage,
    FinishVideo,
    FrontVideo,
}

/// <summary>Durable storage of recordings on the file system (FS1-41, FS1-42).</summary>
public interface IRecordingStore
{
    /// <summary>Creates the (still incomplete) directory of a new recording; <paramref name="mediaDirectory"/> null means the default.</summary>
    Task<RecordingTarget> CreateAsync(DateTimeOffset start, string? mediaDirectory, CancellationToken cancellationToken);

    /// <summary>Writes the metadata last and atomically: only then the recording is complete and listed.</summary>
    Task CompleteAsync(RecordingTarget target, RecordingMetadata metadata, CancellationToken cancellationToken);

    /// <summary>Removes the files of a recording that could not be completed.</summary>
    Task AbandonAsync(RecordingTarget target, CancellationToken cancellationToken);

    /// <summary>Complete recordings of the configured media directory, newest first.</summary>
    Task<IReadOnlyList<RecordingSummary>> ListAsync(CancellationToken cancellationToken);

    Task<Result<RecordingMetadata>> GetAsync(RecordingId id, CancellationToken cancellationToken);

    /// <summary>Path of an existing file of a complete recording.</summary>
    Task<Result<string>> GetFileAsync(RecordingId id, RecordingFile file, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(RecordingId id, CancellationToken cancellationToken);
}

/// <summary>Durable store of the runtime-editable settings.</summary>
public interface ISettingsStore
{
    Task<FinishRecordingSettings> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(FinishRecordingSettings settings, CancellationToken cancellationToken);
}

/// <summary>Live images of the running cameras (FS1-60).</summary>
public enum PreviewKind
{
    FinishCamera,
    FrontCamera,
    FinishStrip,
}

/// <summary>Live preview JPEGs. Preview frames are only produced while at least one client is watching.</summary>
public interface ILivePreview
{
    /// <summary>Registers a watcher until disposed.</summary>
    IDisposable Watch(PreviewKind kind);

    /// <summary>The latest preview image, or null if there is none (cameras stopped, no frames yet).</summary>
    byte[]? Latest(PreviewKind kind);
}

/// <summary>Live push of changes to connected clients.</summary>
public interface IFinishRecordingNotifier
{
    /// <summary>The operating state changed (command, camera health); clients reload the status.</summary>
    void StatusChanged();

    /// <summary>A recording was completed or deleted.</summary>
    void RecordingsChanged();
}
