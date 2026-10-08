using TimingApp.Domain.FinishRecording;

namespace TimingApp.Domain.Tests;

public sealed class FinishEventTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly DetectionSettings Settings = FinishRecordingSettings.Default.Detection;

    /// <summary>Feeds columns at 100 per second from <paramref name="fromSeconds"/> (inclusive) to <paramref name="toSeconds"/> (exclusive).</summary>
    private static List<TrackerStep> Feed(FinishEventTracker tracker, double fromSeconds, double toSeconds, Func<double, bool> occupied)
    {
        var steps = new List<TrackerStep>();
        for (var i = (int)Math.Round(fromSeconds * 100); i < (int)Math.Round(toSeconds * 100); i++)
        {
            var seconds = i / 100.0;
            steps.Add(tracker.Observe(T0.AddSeconds(seconds), occupied(seconds)));
        }
        return steps;
    }

    [Fact]
    public void Observe_FirstOccupiedColumn_StartsEvent()
    {
        var tracker = new FinishEventTracker(Settings);

        var steps = Feed(tracker, 0, 1.01, s => s >= 1.0);

        Assert.Equal(T0.AddSeconds(1), steps[^1].Started);
        Assert.Equal(T0.AddSeconds(1), tracker.CurrentStart);
    }

    [Fact]
    public void Observe_LineFreeForPostRoll_CompletesEventAtEndOfPostRoll()
    {
        var tracker = new FinishEventTracker(Settings);

        var completed = Feed(tracker, 0, 5, s => s is >= 1.0 and <= 2.0).Select(s => s.Completed).OfType<FinishEvent>().Single();

        Assert.Equal(T0.AddSeconds(1), completed.StartedAt);
        Assert.Equal(T0.AddSeconds(3), completed.EndedAt);
        Assert.Equal(FinishEventEnd.PostRoll, completed.EndReason);
        Assert.Null(tracker.CurrentStart);
    }

    [Fact]
    public void Observe_ShortGapShorterThanPostRoll_KeepsOneEvent()
    {
        var tracker = new FinishEventTracker(Settings);

        var completed = Feed(tracker, 0, 6, s => s is >= 1.0 and <= 1.5 or >= 2.2 and <= 2.5).Select(s => s.Completed).OfType<FinishEvent>().ToList();

        var only = Assert.Single(completed);
        Assert.Equal(T0.AddSeconds(3.5), only.EndedAt);
    }

    [Fact]
    public void Observe_MaxDurationReachedWhileOccupied_CompletesAndStartsNewEventImmediately()
    {
        var tracker = new FinishEventTracker(Settings with { MaxDuration = TimeSpan.FromSeconds(60) });

        var steps = Feed(tracker, 0, 95, s => s < 90);
        var completed = steps.Select(s => s.Completed).OfType<FinishEvent>().ToList();

        Assert.Equal(2, completed.Count);
        Assert.Equal(FinishEventEnd.MaxDuration, completed[0].EndReason);
        Assert.Equal(T0, completed[0].StartedAt);
        Assert.Equal(T0.AddSeconds(60), completed[0].EndedAt);
        Assert.Equal(T0.AddSeconds(60), completed[1].StartedAt);
        Assert.Equal(FinishEventEnd.PostRoll, completed[1].EndReason);
    }

    [Fact]
    public void Close_DuringEvent_CompletesWithReasonStopped()
    {
        var tracker = new FinishEventTracker(Settings);
        Feed(tracker, 0, 2, s => s >= 1.0);

        var closed = tracker.Close(T0.AddSeconds(1.99));

        Assert.Equal(new FinishEvent(T0.AddSeconds(1), T0.AddSeconds(1.99), FinishEventEnd.Stopped), closed);
        Assert.Null(tracker.Close(T0.AddSeconds(2)));
    }

    [Fact]
    public void RecordingWindow_AddsPreRollAndFrontRolls()
    {
        var finishEvent = new FinishEvent(T0, T0.AddSeconds(3), FinishEventEnd.PostRoll);

        var window = RecordingWindow.For(finishEvent, Settings);

        Assert.Equal(T0.AddSeconds(-0.5), window.Start);
        Assert.Equal(T0.AddSeconds(3), window.End);
        Assert.Equal(T0.AddSeconds(-2.5), window.FrontStart);
        Assert.Equal(T0.AddSeconds(5), window.FrontEnd);
    }
}
