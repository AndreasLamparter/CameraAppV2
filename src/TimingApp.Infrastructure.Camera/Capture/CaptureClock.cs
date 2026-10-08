namespace TimingApp.Infrastructure.Camera.Capture;

/// <summary>
/// The shared monotonic capture clock of all cameras: <see cref="TimeProvider.GetTimestamp"/> anchored once to UTC,
/// so frame timestamps of different cameras are comparable and unaffected by system clock adjustments (FS1-31).
/// </summary>
public sealed class CaptureClock
{
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _anchorUtc;
    private readonly long _anchorTimestamp;

    public CaptureClock(TimeProvider time)
    {
        _time = time;
        _anchorUtc = time.GetUtcNow();
        _anchorTimestamp = time.GetTimestamp();
    }

    public DateTimeOffset Now() => _anchorUtc + _time.GetElapsedTime(_anchorTimestamp);

    /// <summary>Monotonic timestamp for durations (frame rate, watchdog).</summary>
    public long Timestamp() => _time.GetTimestamp();

    public TimeSpan Elapsed(long since) => _time.GetElapsedTime(since);

    /// <summary>Time zone for displayed clock times (timelines).</summary>
    public TimeZoneInfo LocalTimeZone => _time.LocalTimeZone;
}
