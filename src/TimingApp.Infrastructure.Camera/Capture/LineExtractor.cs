using OpenCvSharp;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Infrastructure.Camera.Capture;

/// <summary>
/// Takes the finish-line strip out of a finish camera frame (FS1-10, FS1-11): the configured pixel column of the turned
/// image (<see cref="FinishLineSettings.Rotation"/>), averaged over the line width, from top to bottom. The frame
/// itself is not turned: the column is read from the corresponding row or column of the camera image. Returns
/// interleaved BGR bytes along the line.
/// </summary>
internal static class LineExtractor
{
    public static unsafe byte[] Extract(Mat frame, FinishLineSettings line)
    {
        if (frame.Type() != MatType.CV_8UC3)
        {
            throw new ArgumentException("Frame must be 8-bit BGR.", nameof(frame));
        }
        int cols = frame.Cols, rows = frame.Rows;
        var swaps = line.SwapsSides;
        var extent = swaps ? rows : cols;
        var width = Math.Min(line.Width, extent);
        var start = Math.Clamp(line.Position, 0, extent - width);
        var length = swaps ? cols : rows;
        var step = (long)frame.Step();
        var data = (byte*)frame.DataPointer;

        // Byte offset of the first pixel of the line in the camera image, and the strides along the line (top to
        // bottom of the turned image) and across it (left to right of the turned image).
        var (first, along, across) = line.Rotation switch
        {
            ImageRotation.Clockwise90 => ((rows - 1 - start) * step, 3L, -step),
            ImageRotation.CounterClockwise90 => ((start * step) + ((cols - 1) * 3L), -3L, step),
            ImageRotation.Rotate180 => (((rows - 1) * step) + ((cols - 1 - start) * 3L), -step, -3L),
            _ => (start * 3L, step, 3L),
        };

        var result = new byte[length * 3];
        for (var i = 0; i < length; i++)
        {
            int b = 0, g = 0, r = 0;
            var pixel = data + first + (i * along);
            for (var k = 0; k < width; k++, pixel += across)
            {
                b += pixel[0];
                g += pixel[1];
                r += pixel[2];
            }
            result[i * 3] = (byte)((b + (width / 2)) / width);
            result[(i * 3) + 1] = (byte)((g + (width / 2)) / width);
            result[(i * 3) + 2] = (byte)((r + (width / 2)) / width);
        }
        return result;
    }

    /// <summary>
    /// The pixels of the camera image the finish line covers: columns (<c>IsColumn</c>) or rows starting at
    /// <c>Start</c>, <see cref="FinishLineSettings.Width"/> wide.
    /// </summary>
    public static (bool IsColumn, int Start) InCameraImage(FinishLineSettings line, int cols, int rows) => line.Rotation switch
    {
        ImageRotation.Clockwise90 => (false, rows - line.Position - line.Width),
        ImageRotation.CounterClockwise90 => (false, line.Position),
        ImageRotation.Rotate180 => (true, cols - line.Position - line.Width),
        _ => (true, line.Position),
    };

    /// <summary>Turns a frame upright for display; returns the frame itself without rotation.</summary>
    public static Mat Upright(Mat frame, ImageRotation rotation)
    {
        RotateFlags? flags = rotation switch
        {
            ImageRotation.Clockwise90 => RotateFlags.Rotate90Clockwise,
            ImageRotation.CounterClockwise90 => RotateFlags.Rotate90Counterclockwise,
            ImageRotation.Rotate180 => RotateFlags.Rotate180,
            _ => null,
        };
        if (flags is not { } rotate)
        {
            return frame;
        }
        var turned = new Mat();
        Cv2.Rotate(frame, turned, rotate);
        return turned;
    }
}
