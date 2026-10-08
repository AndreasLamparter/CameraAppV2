using System.Globalization;
using OpenCvSharp;

namespace TimingApp.Infrastructure.Camera.Rendering;

/// <summary>Timeline below a finish image (FS1-13): a mark every 0.1 s, longer at 0.5 s, the local time at every full second.</summary>
internal static class Timeline
{
    public static readonly long TickInterval = TimeSpan.FromMilliseconds(100).Ticks;
    public static readonly Scalar Background = new(32, 32, 32);
    private static readonly Scalar MarkColor = new(230, 230, 230);

    /// <summary>
    /// Draws the marks between <paramref name="firstUtcTicks"/> and <paramref name="lastUtcTicks"/> into
    /// <paramref name="image"/> from row <paramref name="top"/>; <paramref name="xOf"/> places a mark (null: not shown).
    /// </summary>
    public static void Draw(Mat image, int top, long firstUtcTicks, long lastUtcTicks, Func<long, int?> xOf, TimeZoneInfo zone)
    {
        for (var tick = (firstUtcTicks + TickInterval - 1) / TickInterval * TickInterval; tick <= lastUtcTicks; tick += TickInterval)
        {
            if (xOf(tick) is not { } x)
            {
                continue;
            }
            var tenth = (int)(tick / TickInterval % 10);
            var length = tenth == 0 ? 14 : tenth == 5 ? 9 : 5;
            Cv2.Line(image, new Point(x, top), new Point(x, top + length), MarkColor, 1);
            if (tenth == 0)
            {
                var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(tick, TimeSpan.Zero), zone);
                var label = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                Cv2.PutText(image, label, new Point(x + 2, top + 30), HersheyFonts.HersheySimplex, 0.4, MarkColor, 1, LineTypes.AntiAlias);
            }
        }
    }
}
