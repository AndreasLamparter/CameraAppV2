using OpenCvSharp;
using TimingApp.Application.FinishRecording;

namespace TimingApp.Infrastructure.Camera.Rendering;

/// <summary>
/// Renders the finish image as PNG: the columns side by side (FS1-12) and below them a timeline with a mark every
/// 0.1 s and the local time at every full second (FS1-13). A mark sits at the first column at or after its time.
/// </summary>
internal sealed class FinishImageRenderer(TimeProvider time) : IFinishImageRenderer
{
    public const int TimelineHeight = 36;

    public Task<FinishImageInfo> RenderAsync(IReadOnlyList<LineColumn> columns, bool reverseTimeDirection, string path, CancellationToken cancellationToken) =>
        Task.Run(() => Render(columns, reverseTimeDirection, path), cancellationToken);

    private FinishImageInfo Render(IReadOnlyList<LineColumn> columns, bool reverse, string path)
    {
        using var strip = ColumnImage.Compose(columns.Select(c => c.Bgr).ToList(), reverse);
        using var image = new Mat(strip.Rows + TimelineHeight, strip.Cols, MatType.CV_8UC3, Timeline.Background);
        using (var top = new Mat(image, new Rect(0, 0, strip.Cols, strip.Rows)))
        {
            strip.CopyTo(top);
        }
        DrawTimeline(image, strip.Rows, columns, reverse);
        if (!Cv2.ImWrite(path, image))
        {
            throw new IOException($"Finish image {path} could not be written.");
        }
        return new FinishImageInfo(image.Cols, image.Rows, TimelineHeight);
    }

    private void DrawTimeline(Mat image, int top, IReadOnlyList<LineColumn> columns, bool reverse)
    {
        if (columns.Count == 0)
        {
            return;
        }
        var index = 0;
        int? ColumnAt(long tick)
        {
            while (index < columns.Count && columns[index].Timestamp.UtcTicks < tick)
            {
                index++;
            }
            return index >= columns.Count ? null : reverse ? columns.Count - 1 - index : index;
        }
        Timeline.Draw(image, top, columns[0].Timestamp.UtcTicks, columns[^1].Timestamp.UtcTicks, ColumnAt, time.LocalTimeZone);
    }
}
