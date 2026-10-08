using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Application.FinishRecording;

/// <summary>
/// One camera session from start to stop with its settings snapshot. A single processing loop owns the detection
/// state (background, event tracker, line rate, column buffer) and the assignment of passages; commands and
/// passages reach it only through volatile fields and a concurrent queue.
/// </summary>
internal sealed class CaptureSession
{
    private readonly Func<bool> _manualTrigger;
    private readonly RecordingSaver _saver;
    private readonly IFinishRecordingNotifier _notifier;
    private readonly ILogger _logger;
    private readonly FinishLineMonitor _monitor;
    private readonly FinishEventTracker _tracker;
    private readonly LineRateMeter _rate = new();
    private readonly Queue<LineColumn> _columns = new();
    private readonly ConcurrentQueue<Passage> _incoming = new();
    private readonly List<PassageRecording> _passageRecordings = [];
    private readonly List<(RecordingWindow Window, RecordingPassages Passages)> _recent = [];
    private readonly Task _loop;
    private volatile OperatingMode _mode;
    private volatile RaceName? _race;
    private volatile LineStatus _line = new(LineState.Free, 0, null, false, false, true, false, null, 0);
    private RecordingPassages _eventPassages = new();
    private int _relearnRequested;
    private bool _saveCurrentEvent;
    private int _detectedEvents;

    public CaptureSession(
        ICameraSession camera,
        FinishRecordingSettings settings,
        OperatingMode mode,
        RaceName? race,
        Func<bool> manualTrigger,
        RecordingSaver saver,
        IFinishRecordingNotifier notifier,
        ILogger logger)
    {
        Camera = camera;
        Settings = settings;
        _mode = mode;
        _race = race;
        _manualTrigger = manualTrigger;
        _saver = saver;
        _notifier = notifier;
        _logger = logger;
        _monitor = new FinishLineMonitor(settings.Detection);
        _tracker = new FinishEventTracker(settings.Detection);
        var line = settings.FinishLine;
        var extent = line.ImageWidth(settings.FinishCamera);
        Overlay = new LineOverlay((double)line.Position / extent, (double)line.Width / extent);
        _loop = Task.Run(RunAsync);
    }

    public ICameraSession Camera { get; }

    public FinishRecordingSettings Settings { get; }

    public LineOverlay Overlay { get; }

    public OperatingMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    /// <summary>The race of the following recordings (FS2-02).</summary>
    public RaceName? Race
    {
        get => _race;
        set => _race = value;
    }

    public LineStatus Line => _line;

    public void RequestRelearn() => Interlocked.Exchange(ref _relearnRequested, 1);

    /// <summary>A passage of the timing system (FS2-10); assigned by the processing loop with the next column.</summary>
    public void ReportPassage(Passage passage) => _incoming.Enqueue(passage);

    /// <summary>Stops the cameras; the processing loop drains the remaining columns and completes a running event.</summary>
    public async Task StopAsync()
    {
        await Camera.DisposeAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
    }

    private DetectionSettings Detection => Settings.Detection;

    private async Task RunAsync()
    {
        DateTimeOffset? last = null;
        try
        {
            await foreach (var column in Camera.Columns.ReadAllAsync().ConfigureAwait(false))
            {
                Process(column);
                last = column.Timestamp;
            }
        }
#pragma warning disable CA1031 // The loop must reach the completion of a running event; the failure is logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Processing of finish-line columns failed");
        }

        if (last is { } lastTimestamp)
        {
            AssignPassages(lastTimestamp);
            if (_tracker.Close(lastTimestamp) is { } finishEvent)
            {
                Complete(finishEvent);
            }
            // Stopped: recordings around passages are saved with the columns there are.
            CompletePassageRecordings(DateTimeOffset.MaxValue);
        }
        _line = _line with { State = LineState.Free, EventRunning = false, EventStartedAt = null, OccupancyPercent = 0 };
    }

    private void Process(LineColumn column)
    {
        if (Interlocked.Exchange(ref _relearnRequested, 0) == 1)
        {
            _monitor.Relearn();
        }
        var manual = _manualTrigger();
        var occupancy = _monitor.Measure(column.Bgr, holdBackground: manual || _tracker.CurrentStart is not null);
        var occupied = manual || (!_monitor.IsLearning && occupancy >= Detection.OccupancyThreshold);

        _columns.Enqueue(column);
        _rate.Add(column.Timestamp);
        if (_tracker.CurrentStart is not null && _mode == OperatingMode.Recording)
        {
            _saveCurrentEvent = true;
        }

        var step = _tracker.Observe(column.Timestamp, occupied);
        if (step.Completed is { } completed)
        {
            Complete(completed);
        }
        if (step.Started is { } started)
        {
            _saveCurrentEvent = _mode == OperatingMode.Recording;
            _detectedEvents++;
            _eventPassages = new RecordingPassages();
            AbsorbPassageRecordings(started);
        }
        AssignPassages(column.Timestamp);
        CompletePassageRecordings(column.Timestamp);
        TrimColumns(column.Timestamp);
        Publish(occupancy, occupied);
    }

    private void Complete(FinishEvent finishEvent)
    {
        var save = _saveCurrentEvent;
        _saveCurrentEvent = false;
        var passages = _eventPassages;
        _eventPassages = new RecordingPassages();
        if (!save)
        {
            _logger.LogInformation("Finish event {Start} – {End} detected (not saved)", finishEvent.StartedAt, finishEvent.EndedAt);
            return;
        }
        Save(finishEvent, passages);
    }

    private void Save(FinishEvent finishEvent, RecordingPassages passages)
    {
        var window = RecordingWindow.For(finishEvent, Detection);
        var columns = TimeRange.Covering(_columns.ToList(), c => c.Timestamp, window.Start, window.End);
        _saver.Enqueue(new RecordingDraft(finishEvent, window, columns, Camera.FrontFrames, Settings, _race, passages));
        // Late passages within this window still belong to this recording (FS2-11, FS2-17).
        _recent.Add((window, passages));
    }

    /// <summary>
    /// Assigns the reported passages (FS2-11, FS2-12): to the running saved event when its recording covers the
    /// passage time, else to a recent recording covering it, else to a recording around the passage time, which is
    /// saved once its post-roll has been captured.
    /// </summary>
    private void AssignPassages(DateTimeOffset now)
    {
        while (_incoming.TryDequeue(out var passage))
        {
            var time = passage.Time;
            if (_tracker.CurrentStart is { } start && _saveCurrentEvent && time >= start - Detection.PreRoll)
            {
                _eventPassages.Add(passage);
            }
            else if (_recent.FirstOrDefault(r => r.Window.Start <= time && time <= r.Window.End) is { Passages: { } saved })
            {
                saved.Add(passage);
            }
            else if (_passageRecordings.FirstOrDefault(r => r.Covers(time)) is { } around)
            {
                around.Add(passage);
            }
            else if (_columns.Count > 0 && time - Detection.PreRoll < _columns.Peek().Timestamp && now - time > Passage.MaxReportDelay)
            {
                _logger.LogWarning("Passage of {StartNumber} at {Time} arrived too late; its images are no longer buffered", passage.StartNumber, time);
            }
            else
            {
                _passageRecordings.Add(new PassageRecording(passage, Detection));
            }
        }
    }

    /// <summary>A detected event that covers the passages of a pending recording around them takes them over.</summary>
    private void AbsorbPassageRecordings(DateTimeOffset eventStart)
    {
        if (!_saveCurrentEvent)
        {
            return;
        }
        foreach (var pending in _passageRecordings.Where(r => r.FirstTime >= eventStart - Detection.PreRoll).ToList())
        {
            pending.Passages.Snapshot().ToList().ForEach(_eventPassages.Add);
            _passageRecordings.Remove(pending);
        }
    }

    private void CompletePassageRecordings(DateTimeOffset now)
    {
        foreach (var due in _passageRecordings.Where(r => r.End <= now).ToList())
        {
            _passageRecordings.Remove(due);
            Save(new FinishEvent(due.FirstTime, due.End, FinishEventEnd.Passage), due.Passages);
        }
    }

    /// <summary>
    /// Keeps the pre-roll of the running event, of pending recordings around passages and of a passage reported up to
    /// <see cref="Passage.MaxReportDelay"/> late (plus one column before it); forgets recent recordings no late
    /// passage can reach any more.
    /// </summary>
    private void TrimColumns(DateTimeOffset now)
    {
        var keepFrom = new[] { (_tracker.CurrentStart ?? now) - Detection.PreRoll, now - Passage.MaxReportDelay - Detection.PreRoll }
            .Concat(_passageRecordings.Select(r => r.Start))
            .Min();
        while (_columns.Count > 1 && _columns.ElementAt(1).Timestamp <= keepFrom)
        {
            _columns.Dequeue();
        }
        _recent.RemoveAll(r => r.Window.End < now - Passage.MaxReportDelay - Passage.MaxReportDelay);
    }

    private void Publish(double occupancy, bool occupied)
    {
        var running = _tracker.CurrentStart is not null;
        var state = running && _saveCurrentEvent ? LineState.Recording : running || occupied ? LineState.Occupied : LineState.Free;
        var rate = _rate.Rate;
        var previous = _line;
        _line = new LineStatus(
            state,
            Math.Round(occupancy * 100, 1),
            rate is { } r ? Math.Round(r, 1) : null,
            rate is { } measured && LineRate.IsTooLow(measured, Settings.FinishCamera.FrameRate),
            _manualTrigger(),
            _monitor.IsLearning,
            running,
            _tracker.CurrentStart,
            _detectedEvents);
        if (previous.State != state || previous.BackgroundLearning != _monitor.IsLearning || previous.LineRateWarning != _line.LineRateWarning)
        {
            _notifier.StatusChanged();
        }
    }

    /// <summary>
    /// A recording around passages without a detected event (FS2-12): from the pre-roll before the first to the
    /// post-roll after the last passage; a passage within it extends it.
    /// </summary>
    private sealed class PassageRecording
    {
        private readonly DetectionSettings _detection;

        public PassageRecording(Passage first, DetectionSettings detection)
        {
            _detection = detection;
            FirstTime = LastTime = first.Time;
            Passages.Add(first);
        }

        public RecordingPassages Passages { get; } = new();

        public DateTimeOffset FirstTime { get; private set; }

        public DateTimeOffset LastTime { get; private set; }

        public DateTimeOffset Start => FirstTime - _detection.PreRoll;

        public DateTimeOffset End => LastTime + _detection.PostRoll;

        public bool Covers(DateTimeOffset time) => Start <= time && time <= End;

        public void Add(Passage passage)
        {
            Passages.Add(passage);
            FirstTime = passage.Time < FirstTime ? passage.Time : FirstTime;
            LastTime = passage.Time > LastTime ? passage.Time : LastTime;
        }
    }
}
