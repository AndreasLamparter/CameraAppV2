using OpenCvSharp;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;
using TimingApp.Infrastructure.Camera.Capture;
using TimingApp.Infrastructure.Camera.Rendering;

namespace TimingApp.Infrastructure.Camera.Preview;

/// <summary>Number of clients watching each preview; previews are only produced while it is above zero.</summary>
internal sealed class PreviewWatchers
{
    private readonly int[] _counts = new int[Enum.GetValues<PreviewKind>().Length];

    public bool IsWatched(PreviewKind kind) => Volatile.Read(ref _counts[(int)kind]) > 0;

    public IDisposable Watch(PreviewKind kind)
    {
        Interlocked.Increment(ref _counts[(int)kind]);
        return new Registration(() => Interlocked.Decrement(ref _counts[(int)kind]));
    }

    private sealed class Registration(Action release) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                release();
            }
        }
    }
}

/// <summary>
/// Live image of the finish camera. The capture thread offers a downscaled copy at most every <see cref="Interval"/>
/// while someone watches; JPEG encoding happens on the reading (request) side, never on the capture thread.
/// </summary>
internal sealed class FramePreview(PreviewWatchers watchers, int maxWidth, int jpegQuality, ImageRotation rotation = ImageRotation.None) : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly Lock _lock = new();
    private Mat? _latest;
    private long _version;
    private long _encodedVersion = -1;
    private byte[]? _jpeg;
    private long _lastOffer;

    /// <summary>Called on the capture thread: cheap check, copy only when watched and due.</summary>
    public void Offer(Mat frame)
    {
        if (!watchers.IsWatched(PreviewKind.FinishCamera) || System.Diagnostics.Stopwatch.GetElapsedTime(_lastOffer) < Interval)
        {
            return;
        }
        _lastOffer = System.Diagnostics.Stopwatch.GetTimestamp();
        var copy = Turned(ColumnImage.Downscale(frame, maxWidth));
        Mat? old;
        lock (_lock)
        {
            old = _latest;
            _latest = copy;
            _version++;
        }
        old?.Dispose();
    }

    /// <summary>The downscaled copy turned upright (the preview shows the turned image, like the finish line).</summary>
    private Mat Turned(Mat copy)
    {
        var turned = LineExtractor.Upright(copy, rotation);
        if (!ReferenceEquals(turned, copy))
        {
            copy.Dispose();
        }
        return turned;
    }

    public byte[]? LatestJpeg()
    {
        lock (_lock)
        {
            if (_latest is null)
            {
                return null;
            }
            if (_encodedVersion != _version)
            {
                _jpeg = ColumnImage.EncodeJpeg(_latest, jpegQuality);
                _encodedVersion = _version;
            }
            return _jpeg;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _latest?.Dispose();
            _latest = null;
        }
    }
}

/// <summary>
/// The running finish image of the last seconds (FS1-60) on a fixed time axis: one slot per nominal frame interval,
/// each showing the newest column captured at or before its time, so frame rate fluctuations neither stretch nor
/// compress the image. A slot without a column for longer than <see cref="MaxHold"/> stays black (camera stalled).
/// Below it a timeline with evenly spaced 0.1 s marks. Rendered on request, at most every <see cref="RenderInterval"/>.
/// </summary>
internal sealed class LiveStrip(TimeSpan duration, double frameRate, bool reverse, int jpegQuality, TimeZoneInfo zone)
{
    public static readonly TimeSpan MaxHold = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(150);
    private const int StripHeight = 240;
    private const int TimelineHeight = 36;
    private const int TargetWidth = 960;

    private readonly Lock _lock = new();
    private readonly Queue<LineColumn> _columns = new();
    private readonly Lock _renderLock = new();
    private readonly int _slots = Math.Max(1, (int)Math.Round(duration.TotalSeconds * frameRate));
    private byte[]? _jpeg;
    private long _renderedAt;

    /// <summary>Called on the capture thread: only stores a reference to the immutable column.</summary>
    public void Add(LineColumn column)
    {
        lock (_lock)
        {
            _columns.Enqueue(column);
            var oldest = column.Timestamp - duration - MaxHold;
            while (_columns.Peek().Timestamp < oldest)
            {
                _columns.Dequeue();
            }
        }
    }

    public byte[]? LatestJpeg()
    {
        lock (_renderLock)
        {
            if (_jpeg is not null && System.Diagnostics.Stopwatch.GetElapsedTime(_renderedAt) < RenderInterval)
            {
                return _jpeg;
            }
            List<LineColumn> columns;
            lock (_lock)
            {
                columns = [.. _columns];
            }
            if (columns.Count == 0)
            {
                return null;
            }
            using var image = Render(columns);
            _jpeg = ColumnImage.EncodeJpeg(image, jpegQuality);
            _renderedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            return _jpeg;
        }
    }

    /// <summary>
    /// For each slot (oldest first, the last one at <paramref name="end"/>, one every 1/<paramref name="rate"/> s) the
    /// index of the newest column at or before the slot time, or -1 when there is none within <paramref name="maxHold"/>.
    /// </summary>
    internal static int[] Slots(IReadOnlyList<LineColumn> columns, DateTimeOffset end, double rate, int count, TimeSpan maxHold)
    {
        var result = new int[count];
        var next = 0;
        for (var slot = 0; slot < count; slot++)
        {
            var at = end - TimeSpan.FromSeconds((count - 1 - slot) / rate);
            while (next < columns.Count && columns[next].Timestamp <= at)
            {
                next++;
            }
            var index = next - 1;
            result[slot] = index >= 0 && at - columns[index].Timestamp <= maxHold ? index : -1;
        }
        return result;
    }

    private Mat Render(List<LineColumn> columns)
    {
        var end = columns[^1].Timestamp;
        var slots = Slots(columns, end, frameRate, _slots, MaxHold);
        var black = new byte[columns[^1].Bgr.Length];
        using var strip = ColumnImage.Compose([.. slots.Select(i => i < 0 ? black : columns[i].Bgr)], reverse);
        var scale = Math.Max(1, TargetWidth / _slots);
        var width = _slots * scale;
        var image = new Mat(StripHeight + TimelineHeight, width, MatType.CV_8UC3, Timeline.Background);
        using (var top = new Mat(image, new Rect(0, 0, width, StripHeight)))
        {
            Cv2.Resize(strip, top, new Size(width, StripHeight), interpolation: InterpolationFlags.Area);
        }
        var slotTicks = TimeSpan.FromSeconds(1 / frameRate).Ticks;
        var first = end.UtcTicks - ((_slots - 1) * slotTicks);
        int? XOf(long tick)
        {
            var slot = (int)Math.Round((double)(tick - first) / slotTicks);
            return slot < 0 || slot >= _slots ? null : ((reverse ? _slots - 1 - slot : slot) * scale) + (scale / 2);
        }
        Timeline.Draw(image, StripHeight, first, end.UtcTicks, XOf, zone);
        return image;
    }
}
