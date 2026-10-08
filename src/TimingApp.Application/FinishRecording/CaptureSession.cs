using Microsoft.Extensions.Logging;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Application.FinishRecording;

/// <summary>
/// One camera session from start to stop with its settings snapshot. A single processing loop owns the detection
/// state (background, event tracker, line rate, column buffer); commands reach it only through volatile flags.
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
    private readonly Task _loop;
    private volatile OperatingMode _mode;
    private volatile LineStatus _line = new(LineState.Free, 0, null, false, false, true, false, null, 0);
    private int _relearnRequested;
    private bool _saveCurrentEvent;
    private int _detectedEvents;

    public CaptureSession(
        ICameraSession camera,
        FinishRecordingSettings settings,
        OperatingMode mode,
        Func<bool> manualTrigger,
        RecordingSaver saver,
        IFinishRecordingNotifier notifier,
        ILogger logger)
    {
        Camera = camera;
        Settings = settings;
        _mode = mode;
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

    public LineStatus Line => _line;

    public void RequestRelearn() => Interlocked.Exchange(ref _relearnRequested, 1);

    /// <summary>Stops the cameras; the processing loop drains the remaining columns and completes a running event.</summary>
    public async Task StopAsync()
    {
        await Camera.DisposeAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
    }

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

        if (last is { } lastTimestamp && _tracker.Close(lastTimestamp) is { } finishEvent)
        {
            Complete(finishEvent);
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
        var occupied = manual || (!_monitor.IsLearning && occupancy >= Settings.Detection.OccupancyThreshold);

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
        if (step.Started is not null)
        {
            _saveCurrentEvent = _mode == OperatingMode.Recording;
            _detectedEvents++;
        }
        TrimColumns(column.Timestamp);
        Publish(occupancy, occupied);
    }

    private void Complete(FinishEvent finishEvent)
    {
        var save = _saveCurrentEvent;
        _saveCurrentEvent = false;
        if (!save)
        {
            _logger.LogInformation("Finish event {Start} – {End} detected (not saved)", finishEvent.StartedAt, finishEvent.EndedAt);
            return;
        }
        var window = RecordingWindow.For(finishEvent, Settings.Detection);
        var columns = TimeRange.Covering(_columns.ToList(), c => c.Timestamp, window.Start, window.End);
        _saver.Enqueue(new RecordingDraft(finishEvent, window, columns, Camera.FrontFrames, Settings));
    }

    /// <summary>Keeps the pre-roll before now, or everything since the pre-roll of the running event (plus one column before it).</summary>
    private void TrimColumns(DateTimeOffset now)
    {
        var keepFrom = (_tracker.CurrentStart ?? now) - Settings.Detection.PreRoll;
        while (_columns.Count > 1 && _columns.ElementAt(1).Timestamp <= keepFrom)
        {
            _columns.Dequeue();
        }
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
}
