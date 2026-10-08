using Microsoft.AspNetCore.SignalR;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Api.Live;

/// <summary>
/// Live push: "status" with the full <see cref="FinishRecordingStatus"/> and "recordingsChanged" as a reload hint.
/// Clients fall back to polling while disconnected. Requires a signed-in operator.
/// </summary>
internal sealed class FinishHub : Hub;

/// <summary>
/// Pushes the status on changes (coalesced) and periodically while cameras run or recordings are being saved, so
/// measured rates and camera errors stay current. The service is resolved lazily (it depends on this notifier).
/// </summary>
internal sealed class LiveStatusPublisher(IHubContext<FinishHub> hub, IServiceProvider services, ILogger<LiveStatusPublisher> logger)
    : BackgroundService, IFinishRecordingNotifier
{
    private static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(500);
    private int _statusPending;
    private int _recordingsPending;

    public void StatusChanged() => Interlocked.Exchange(ref _statusPending, 1);

    public void RecordingsChanged() => Interlocked.Exchange(ref _recordingsPending, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var service = services.GetRequiredService<FinishRecordingService>();
        var sinceLastPeriodic = TimeSpan.Zero;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Coalesce, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            sinceLastPeriodic += Coalesce;
            var status = service.GetStatus();
            var periodic = sinceLastPeriodic >= Period && (status.Mode != OperatingMode.Stopped || status.PendingSaves > 0);
            if (Interlocked.Exchange(ref _statusPending, 0) == 1 || periodic)
            {
                sinceLastPeriodic = TimeSpan.Zero;
                await SendAsync("status", status);
            }
            if (Interlocked.Exchange(ref _recordingsPending, 0) == 1)
            {
                await SendAsync("recordingsChanged", null);
            }
        }
    }

    private async Task SendAsync(string method, object? payload)
    {
        try
        {
            await (payload is null ? hub.Clients.All.SendAsync(method) : hub.Clients.All.SendAsync(method, payload));
        }
#pragma warning disable CA1031 // A failed push must never affect the recording; clients also poll.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogDebug(ex, "Push {Method} failed", method);
        }
    }
}
