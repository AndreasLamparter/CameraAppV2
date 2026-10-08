using System.Collections.Concurrent;
using OpenCvSharp;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;
using TimingApp.Infrastructure.Camera.Sources;

namespace TimingApp.Infrastructure.Tests;

/// <summary>Time that only moves when told to; for deterministic capture timestamps.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private long _timestamp = 1;

    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan by)
    {
        Now += by;
        Interlocked.Add(ref _timestamp, by.Ticks);
    }
}

/// <summary>A camera source with uniform frames of a given gray value; can fail to open or deliver nothing.</summary>
internal sealed class FakeSource(int width, int height, byte gray, bool failOpen = false, bool noFrames = false, string? warning = null) : ICameraSource
{
    public string? Warning => warning;

    public bool Disposed { get; private set; }

    public void Open()
    {
        if (failOpen)
        {
            throw new CameraOpenException("not connected");
        }
    }

    public bool Grab()
    {
        Thread.Sleep(2);
        return !noFrames;
    }

    public bool Retrieve(Mat frame)
    {
        frame.Create(height, width, MatType.CV_8UC3);
        frame.SetTo(new Scalar(gray, gray, gray));
        return true;
    }

    public void Dispose() => Disposed = true;
}

/// <summary>
/// A camera delivering JPEGs like a MJPG camera: frame n is a uniform gray of 10 + 3n (n &lt; 60), so the order of
/// decoded columns is visible. Keeps every JPEG it handed out.
/// </summary>
internal sealed class FakeJpegSource(int width, int height) : ICameraSource
{
    private byte[]? _current;
    private int _count;

    public List<byte[]> Produced { get; } = [];

    public void Open()
    {
    }

    public bool Grab()
    {
        Thread.Sleep(2);
        var gray = (byte)(10 + (3 * (_count++ % 60)));
        using var frame = new Mat(height, width, MatType.CV_8UC3, new Scalar(gray, gray, gray));
        Cv2.ImEncode(".jpg", frame, out var jpeg);
        _current = jpeg;
        lock (Produced)
        {
            Produced.Add(jpeg);
        }
        return true;
    }

    public byte[]? CopyJpeg() => _current;

    public bool Retrieve(Mat frame) => throw new InvalidOperationException("A JPEG camera is decoded by the pipeline, not on the capture thread.");

    public void Dispose()
    {
    }
}

internal sealed class FakeSourceFactory(Func<CameraRole, ICameraSource> create, bool simulated = true) : ICameraSourceFactory
{
    public bool IsSimulated => simulated;

    /// <summary>Settings the sources were created with, i.e. with the resolved device index.</summary>
    public ConcurrentDictionary<CameraRole, CameraSettings> Created { get; } = new();

    public ICameraSource Create(CameraRole role, CameraSettings settings, FinishLineSettings line)
    {
        Created[role] = settings;
        return create(role);
    }
}

internal sealed class FakeDeviceEnumerator(params EnumeratedDevice[] devices) : ICameraDeviceEnumerator
{
    public IReadOnlyList<EnumeratedDevice> List(bool includeModes = true) => devices;
}
