using System.Runtime.InteropServices;
using OpenCvSharp;

namespace TimingApp.Infrastructure.Camera.Rendering;

/// <summary>Image helpers shared by the finish image renderer and the live preview.</summary>
internal static class ColumnImage
{
    /// <summary>
    /// Sets columns (BGR bytes along the line, all of equal length) side by side: column i at x = i, or at
    /// x = count - 1 - i when the time direction is reversed (FS1-12). The caller owns the returned <see cref="Mat"/>.
    /// </summary>
    public static Mat Compose(IReadOnlyList<byte[]> columns, bool reverse)
    {
        var count = columns.Count;
        var length = count == 0 ? 0 : columns[0].Length / 3;
        var mat = new Mat(Math.Max(length, 1), Math.Max(count, 1), MatType.CV_8UC3, Scalar.Black);
        if (count == 0 || length == 0)
        {
            return mat;
        }
        var pixels = new byte[length * count * 3];
        for (var i = 0; i < count; i++)
        {
            var x = reverse ? count - 1 - i : i;
            var column = columns[i];
            var rows = Math.Min(length, column.Length / 3);
            for (var y = 0; y < rows; y++)
            {
                var target = ((y * count) + x) * 3;
                pixels[target] = column[y * 3];
                pixels[target + 1] = column[(y * 3) + 1];
                pixels[target + 2] = column[(y * 3) + 2];
            }
        }
        Marshal.Copy(pixels, 0, mat.Data, pixels.Length);
        return mat;
    }

    public static byte[] EncodeJpeg(Mat image, int quality)
    {
        Cv2.ImEncode(".jpg", image, out var bytes, new ImageEncodingParam(ImwriteFlags.JpegQuality, quality));
        return bytes;
    }

    /// <summary>Downscaled copy whose width does not exceed <paramref name="maxWidth"/> (a plain copy when smaller).</summary>
    public static Mat Downscale(Mat image, int maxWidth, int maxHeight = int.MaxValue)
    {
        var scale = Math.Min(1.0, Math.Min((double)maxWidth / image.Cols, (double)maxHeight / image.Rows));
        if (scale >= 1.0)
        {
            return image.Clone();
        }
        var result = new Mat();
        Cv2.Resize(image, result, new Size(Math.Max(1, (int)(image.Cols * scale)), Math.Max(1, (int)(image.Rows * scale))), interpolation: InterpolationFlags.Area);
        return result;
    }
}
