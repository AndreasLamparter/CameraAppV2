using Microsoft.Extensions.Options;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Api.Hosting;

/// <summary>Start-up and shut-down behavior (<c>TimingApp:FinishRecording</c>).</summary>
public sealed class FinishRecordingOptions
{
    public const string Section = "TimingApp:FinishRecording";

    /// <summary>Mode entered right after start-up (FS1-04), e.g. <c>--TimingApp:FinishRecording:StartupMode=Recording</c>.</summary>
    public OperatingMode StartupMode { get; set; } = OperatingMode.Stopped;

    /// <summary>How long shutting down may take to finish saving recordings.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Enters the start-up mode and, on shutdown, stops the cameras and waits for pending recordings (FS1-50).</summary>
internal sealed class FinishRecordingLifecycle(
    FinishRecordingService service,
    IOptions<FinishRecordingOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<FinishRecordingLifecycle> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var mode = options.Value.StartupMode;
        if (mode != OperatingMode.Stopped)
        {
            // After the server is up, so a slow camera driver does not delay the web interface.
            lifetime.ApplicationStarted.Register(() => _ = StartCamerasAsync(mode));
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Shutting down: stopping cameras and saving pending recordings");
        await service.ShutdownAsync(cancellationToken);
    }

    private async Task StartCamerasAsync(OperatingMode mode)
    {
        try
        {
            if (mode == OperatingMode.Recording)
            {
                // Recording needs a race name from the dialog (FS2-01, FS2-08): start in Preview until it is given.
                logger.LogInformation("Start-up mode Recording needs a race name; starting in Preview");
                mode = OperatingMode.Preview;
            }
            await service.StartAsync(mode, null, CancellationToken.None);
        }
#pragma warning disable CA1031 // Start-up must not crash the host; the failure is logged and the operator can retry.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Starting the cameras in start-up mode {Mode} failed", mode);
        }
    }
}
