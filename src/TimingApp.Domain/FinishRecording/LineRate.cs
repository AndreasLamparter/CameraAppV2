namespace TimingApp.Domain.FinishRecording;

/// <summary>
/// Measures the line rate (columns per second) from the column timestamps of the last seconds (FS1-14).
/// Not thread-safe: owned by one processing loop.
/// </summary>
public sealed class LineRateMeter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinimumSpan = TimeSpan.FromSeconds(1);
    private readonly Queue<DateTimeOffset> _timestamps = new();

    public void Add(DateTimeOffset timestamp)
    {
        _timestamps.Enqueue(timestamp);
        while (_timestamps.Count > 0 && timestamp - _timestamps.Peek() > Window)
        {
            _timestamps.Dequeue();
        }
    }

    /// <summary>Columns per second, or null until at least one second has been observed.</summary>
    public double? Rate
    {
        get
        {
            if (_timestamps.Count < 2)
            {
                return null;
            }
            var span = _timestamps.Last() - _timestamps.Peek();
            return span < MinimumSpan ? null : (_timestamps.Count - 1) / span.TotalSeconds;
        }
    }
}

/// <summary>Line rate rule of FS1-14.</summary>
public static class LineRate
{
    /// <summary>A measured line rate below this fraction of the configured frame rate is reported as a warning.</summary>
    public const double WarningRatio = 0.95;

    public static bool IsTooLow(double measured, int configuredFrameRate) => measured < configuredFrameRate * WarningRatio;
}
