using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.SharedKernel;

namespace TimingApp.Infrastructure.Camera.Video;

/// <summary>
/// Encodes H.264 / yuv420p MP4 with <c>+faststart</c> through the external ffmpeg process (browser playback and
/// seeking). A missing ffmpeg yields <c>video.ffmpegMissing</c>, a failed encoding <c>video.encodingFailed</c>.
/// </summary>
internal sealed class FfmpegVideoEncoder(CameraOptions options, ILogger<FfmpegVideoEncoder> logger) : IVideoEncoder
{
    /// <summary>Width of the window that scrolls over the finish image in the finish video.</summary>
    public const int FinishVideoWidth = 640;

    public Task<Result> EncodeFinishVideoAsync(string imagePath, FinishImageInfo image, double lineRate, bool reverseTimeDirection, string outputPath, CancellationToken cancellationToken) =>
        RunAsync(FinishVideoArguments(imagePath, image, lineRate, reverseTimeDirection, outputPath), null, outputPath, cancellationToken);

    public Task<Result> EncodeFrontVideoAsync(IReadOnlyList<EncodedFrame> frames, double frameRate, string outputPath, CancellationToken cancellationToken) =>
        RunAsync(FrontVideoArguments(frameRate, outputPath), frames, outputPath, cancellationToken);

    /// <summary>
    /// Scrolls a window over the finish image, one column per frame at the line rate: at frame n the column of
    /// time n is at the leading edge (right edge, or left edge with reversed time direction).
    /// </summary>
    internal static List<string> FinishVideoArguments(string imagePath, FinishImageInfo image, double lineRate, bool reverse, string outputPath)
    {
        var width = Even(Math.Min(FinishVideoWidth, image.Width));
        var height = Even(image.Height);
        var maxX = image.Width - width;
        var x = reverse
            ? $"max(min({Number(image.Width - 1)}-n\\,{Number(maxX)})\\,0)"
            : $"min(max(n-{Number(width - 1)}\\,0)\\,{Number(maxX)})";
        return
        [
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-loop", "1", "-framerate", Number(lineRate), "-i", imagePath,
            "-vf", $"crop={Number(width)}:{Number(height)}:{x}:0,format=yuv420p",
            "-frames:v", Number(image.Width),
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart",
            outputPath,
        ];
    }

    /// <summary>JPEG frames from stdin at a constant frame rate, frame for frame (no drops or duplicates).</summary>
    internal static List<string> FrontVideoArguments(double frameRate, string outputPath) =>
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "image2pipe", "-c:v", "mjpeg", "-framerate", Number(frameRate), "-i", "-",
        "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p",
        "-fps_mode", "passthrough",
        "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p", "-movflags", "+faststart",
        outputPath,
    ];

    private async Task<Result> RunAsync(List<string> arguments, IReadOnlyList<EncodedFrame>? input, string outputPath, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(options.FfmpegPath)
        {
            RedirectStandardInput = input is not null,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        arguments.ForEach(start.ArgumentList.Add);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new Win32Exception("ffmpeg did not start.");
        }
        catch (Win32Exception ex)
        {
            logger.LogError("ffmpeg could not be started from {Path}: {Reason}", options.FfmpegPath, ex.Message);
            return Error.Unavailable("video.ffmpegMissing");
        }

        using (process)
        {
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            try
            {
                if (input is not null)
                {
                    await WriteFramesAsync(process, input, cancellationToken).ConfigureAwait(false);
                }
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            var stderr = await errors.ConfigureAwait(false);
            await output.ConfigureAwait(false);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                return Result.Success();
            }
            logger.LogError("ffmpeg failed with exit code {ExitCode}: {Errors}", process.ExitCode, Tail(stderr));
            TryDelete(outputPath);
            return Error.Unavailable("video.encodingFailed");
        }
    }

    private static async Task WriteFramesAsync(Process process, IReadOnlyList<EncodedFrame> frames, CancellationToken cancellationToken)
    {
        var stdin = process.StandardInput.BaseStream;
        try
        {
            foreach (var frame in frames)
            {
                await stdin.WriteAsync(frame.Jpeg, cancellationToken).ConfigureAwait(false);
            }
            await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // ffmpeg ended early; its exit code and error output tell why.
        }
        finally
        {
            process.StandardInput.Close();
        }
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int Even(int value) => Math.Max(2, value - (value % 2));

    private static string Tail(string text) => text.Length <= 2000 ? text : text[^2000..];

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
