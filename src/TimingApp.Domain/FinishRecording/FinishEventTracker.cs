namespace TimingApp.Domain.FinishRecording;

/// <summary>Why a finish event ended.</summary>
public enum FinishEventEnd
{
    /// <summary>The line stayed free for the post-roll (FS1-21).</summary>
    PostRoll,

    /// <summary>The maximum duration was reached (FS1-23).</summary>
    MaxDuration,

    /// <summary>The cameras were stopped during the event (FS1-03).</summary>
    Stopped,

    /// <summary>No event was detected; the recording was made around a reported passage (FS2-12).</summary>
    Passage,
}

/// <summary>A completed finish event: from the first occupied column to the end of the post-roll (or the cut).</summary>
public sealed record FinishEvent(DateTimeOffset StartedAt, DateTimeOffset EndedAt, FinishEventEnd EndReason);

/// <summary>Outcome of one observed column: an event that just completed and/or an event that just started.</summary>
public readonly record struct TrackerStep(FinishEvent? Completed, DateTimeOffset? Started);

/// <summary>
/// Finish event state machine over the column stream (FS1-21, FS1-23, FS1-24). An event starts with the first
/// occupied column and ends when the line has been free for the post-roll. An event reaching the maximum duration is
/// completed; if the line is still occupied, a new event starts at the same column. Not thread-safe.
/// </summary>
public sealed class FinishEventTracker(DetectionSettings settings)
{
    private DateTimeOffset? _start;
    private DateTimeOffset _lastOccupied;

    /// <summary>Start of the running event, or null when no event is running.</summary>
    public DateTimeOffset? CurrentStart => _start;

    /// <param name="timestamp">Timestamp of the column.</param>
    /// <param name="occupied">Measured occupancy reached the threshold or the manual trigger is active.</param>
    public TrackerStep Observe(DateTimeOffset timestamp, bool occupied)
    {
        if (_start is not { } start)
        {
            return occupied ? new TrackerStep(null, Begin(timestamp)) : default;
        }

        if (occupied)
        {
            _lastOccupied = timestamp;
        }
        if (timestamp - start >= settings.MaxDuration)
        {
            _start = null;
            var completed = new FinishEvent(start, timestamp, FinishEventEnd.MaxDuration);
            return new TrackerStep(completed, occupied ? Begin(timestamp) : null);
        }
        if (!occupied && timestamp - _lastOccupied >= settings.PostRoll)
        {
            _start = null;
            return new TrackerStep(new FinishEvent(start, timestamp, FinishEventEnd.PostRoll), null);
        }
        return default;
    }

    /// <summary>Completes a running event at the last column (cameras stopped); null if no event is running.</summary>
    public FinishEvent? Close(DateTimeOffset lastTimestamp)
    {
        if (_start is not { } start)
        {
            return null;
        }
        _start = null;
        return new FinishEvent(start, lastTimestamp < start ? start : lastTimestamp, FinishEventEnd.Stopped);
    }

    private DateTimeOffset Begin(DateTimeOffset timestamp)
    {
        _start = timestamp;
        _lastOccupied = timestamp;
        return timestamp;
    }
}

/// <summary>Time span of a recording (FS1-22) and of its front video (FS1-30).</summary>
public sealed record RecordingWindow(DateTimeOffset Start, DateTimeOffset End, DateTimeOffset FrontStart, DateTimeOffset FrontEnd)
{
    public static RecordingWindow For(FinishEvent finishEvent, DetectionSettings settings)
    {
        var start = finishEvent.StartedAt - settings.PreRoll;
        var end = finishEvent.EndedAt;
        return new RecordingWindow(start, end, start - settings.FrontPreRoll, end + settings.FrontPostRoll);
    }
}
