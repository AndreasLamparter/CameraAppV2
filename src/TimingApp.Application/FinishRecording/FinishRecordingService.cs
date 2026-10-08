using Microsoft.Extensions.Logging;
using TimingApp.Domain.FinishRecording;
using TimingApp.Domain.SharedKernel;

namespace TimingApp.Application.FinishRecording;

/// <summary>
/// Operating states and commands of the finish recording (FS1-01 to FS1-05, FS1-24, FS1-25, FS1-50). Commands are
/// serialized; each camera start creates a <see cref="CaptureSession"/> with a settings snapshot (FS1-44).
/// </summary>
public sealed class FinishRecordingService(
    ICameraSystem cameras,
    ISettingsStore settingsStore,
    RecordingSaver saver,
    IFinishRecordingNotifier notifier,
    TimeProvider time,
    ILogger<FinishRecordingService> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _commands = new(1, 1);
    private volatile CaptureSession? _session;
    private volatile bool _manualTrigger;
    private volatile bool _disposed;

    public OperatingMode Mode => _session?.Mode ?? OperatingMode.Stopped;

    /// <summary>Starts the cameras in the given mode, or switches the mode of running cameras without restarting them (FS1-02).</summary>
    public async Task<Result> StartAsync(OperatingMode mode, CancellationToken cancellationToken)
    {
        if (mode == OperatingMode.Stopped)
        {
            return Error.Validation("control.modeInvalid");
        }
        await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is { } running)
            {
                running.Mode = mode;
                logger.LogInformation("Mode switched to {Mode}", mode);
            }
            else
            {
                var settings = await settingsStore.GetAsync(cancellationToken).ConfigureAwait(false);
                var options = new CameraSessionOptions(
                    settings.FinishCamera,
                    settings.FinishLine,
                    settings.FrontCameraEnabled ? settings.FrontCamera : null,
                    FrontRetention(settings.Detection));
                var cameraSession = await cameras.StartAsync(options, cancellationToken).ConfigureAwait(false);
                _session = new CaptureSession(cameraSession, settings, mode, () => _manualTrigger, saver, notifier, logger);
                logger.LogInformation("Cameras started in mode {Mode}", mode);
            }
        }
        finally
        {
            _commands.Release();
        }
        notifier.StatusChanged();
        return Result.Success();
    }

    /// <summary>Stops the cameras. A running event is still completed and, in mode Recording, saved (FS1-03).</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            // Host already shut down (a second stop request after disposal): nothing is running any more.
            return;
        }
        await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is { } running)
            {
                _session = null;
                await running.StopAsync().ConfigureAwait(false);
                logger.LogInformation("Cameras stopped");
            }
            _manualTrigger = false;
        }
        finally
        {
            _commands.Release();
        }
        notifier.StatusChanged();
    }

    /// <summary>Sets or resets the manual trigger: while active, the line counts as occupied (FS1-24).</summary>
    public void SetManualTrigger(bool active)
    {
        _manualTrigger = active;
        logger.LogInformation("Manual trigger {State}", active ? "set" : "reset");
        notifier.StatusChanged();
    }

    /// <summary>Learns the background of the finish line again (FS1-25).</summary>
    public void RelearnBackground()
    {
        _session?.RequestRelearn();
        notifier.StatusChanged();
    }

    /// <summary>Stops the cameras and waits until every recording has been saved (FS1-50: shut down).</summary>
    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await saver.DrainAsync(cancellationToken).ConfigureAwait(false);
    }

    public FinishRecordingStatus GetStatus()
    {
        var session = _session;
        var line = session?.Line ?? new LineStatus(LineState.Free, 0, null, false, _manualTrigger, false, false, null, 0);
        return new FinishRecordingStatus(
            session?.Mode ?? OperatingMode.Stopped,
            CameraStatusOf(session, CameraRole.Finish),
            CameraStatusOf(session, CameraRole.Front),
            line with { ManualTrigger = _manualTrigger },
            session?.Overlay,
            saver.Pending,
            saver.LastProblem,
            time.GetUtcNow());
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_session is { } running)
        {
            _session = null;
            await running.StopAsync().ConfigureAwait(false);
        }
        // _commands is not disposed: a late command must not fail with ObjectDisposedException (no wait handle is used).
    }

    private static CameraStatus CameraStatusOf(CaptureSession? session, CameraRole role)
    {
        if (session is null)
        {
            return new CameraStatus(CameraState.Off, null, 0, 0);
        }
        var health = session.Camera.Health(role);
        var configured = role == CameraRole.Finish ? session.Settings.FinishCamera.FrameRate : session.Settings.FrontCamera.FrameRate;
        return new CameraStatus(health.State, health.ErrorCode, Math.Round(health.MeasuredFrameRate, 1), configured, health.WarningCode);
    }

    /// <summary>How long front frames must stay buffered: longest recording plus both front rolls and the save wait.</summary>
    private static TimeSpan FrontRetention(DetectionSettings d) =>
        d.FrontPreRoll + d.PreRoll + d.MaxDuration + d.PostRoll + d.FrontPostRoll + RecordingSaver.FrontWaitMargin + TimeSpan.FromSeconds(2);
}
