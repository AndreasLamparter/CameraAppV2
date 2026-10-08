namespace TimingApp.Domain.FinishRecording;

/// <summary>
/// Learns the background of the empty finish line and measures its occupancy (FS1-20, FS1-25): the fraction of
/// pixels whose gray value differs from the background by more than the pixel threshold. While the line is free,
/// the background follows slow lighting changes; it is held while a finish event runs or the manual trigger is
/// active, so a rider whose colors resemble the background cannot be learned into it. Not thread-safe: owned by one
/// processing loop.
/// </summary>
public sealed class FinishLineMonitor(DetectionSettings settings)
{
    /// <summary>Number of columns averaged into a freshly learned background.</summary>
    public const int LearningColumns = 30;

    /// <summary>Weight of a new free column in the background (exponential moving average).</summary>
    public const float AdaptationRate = 0.02f;

    private float[]? _background;
    private int _learned;

    /// <summary>True while the background is being (re)learned; occupancy is reported as 0 meanwhile.</summary>
    public bool IsLearning => _background is null || _learned < LearningColumns;

    /// <summary>Discards the background; it is learned again from the next columns.</summary>
    public void Relearn()
    {
        _background = null;
        _learned = 0;
    }

    /// <summary>
    /// Measures one column given as interleaved BGR bytes and returns its occupancy (0..1).
    /// <paramref name="holdBackground"/> (running event, manual trigger) suppresses background adaptation.
    /// </summary>
    public double Measure(ReadOnlySpan<byte> bgr, bool holdBackground)
    {
        var length = bgr.Length / 3;
        if (length == 0)
        {
            return 0;
        }
        if (_background is null || _background.Length != length)
        {
            _background = new float[length];
            _learned = 0;
        }

        if (_learned < LearningColumns)
        {
            Learn(bgr, _background, ++_learned);
            return 0;
        }

        var changed = 0;
        for (var i = 0; i < length; i++)
        {
            if (MathF.Abs(Gray(bgr, i) - _background[i]) > settings.PixelThreshold)
            {
                changed++;
            }
        }
        var occupancy = (double)changed / length;

        if (!holdBackground && occupancy < settings.OccupancyThreshold)
        {
            for (var i = 0; i < length; i++)
            {
                _background[i] += AdaptationRate * (Gray(bgr, i) - _background[i]);
            }
        }
        return occupancy;
    }

    private static void Learn(ReadOnlySpan<byte> bgr, float[] background, int count)
    {
        for (var i = 0; i < background.Length; i++)
        {
            background[i] += (Gray(bgr, i) - background[i]) / count;
        }
    }

    /// <summary>ITU-R BT.601 luma of pixel <paramref name="index"/>.</summary>
    private static float Gray(ReadOnlySpan<byte> bgr, int index) =>
        (0.114f * bgr[index * 3]) + (0.587f * bgr[(index * 3) + 1]) + (0.299f * bgr[(index * 3) + 2]);
}
