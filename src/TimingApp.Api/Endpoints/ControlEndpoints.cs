using TimingApp.Api.Access;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Api.Endpoints;

public sealed record StartRequest(OperatingMode Mode);

public sealed record TriggerRequest(bool Active);

/// <summary>Operating commands for the UI and for external programs (FS1-50, FS1-60).</summary>
internal static class ControlEndpoints
{
    public static void MapControlEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/control").WithTags("Control").RequireAuthorization(AccessPolicies.Control);

        group.MapGet("/status", (FinishRecordingService service) => Results.Ok(service.GetStatus()))
            .Produces<FinishRecordingStatus>();

        group.MapPost("/start", async (StartRequest request, FinishRecordingService service, CancellationToken ct) =>
        {
            var result = await service.StartAsync(request.Mode, ct);
            return result.IsSuccess ? Results.Ok(service.GetStatus()) : HttpResults.Problem(result.Error!);
        }).Produces<FinishRecordingStatus>().ProducesErrors(400);

        group.MapPost("/stop", async (FinishRecordingService service, CancellationToken ct) =>
        {
            await service.StopAsync(ct);
            return Results.Ok(service.GetStatus());
        }).Produces<FinishRecordingStatus>();

        group.MapPut("/trigger", (TriggerRequest request, FinishRecordingService service) =>
        {
            service.SetManualTrigger(request.Active);
            return Results.Ok(service.GetStatus());
        }).Produces<FinishRecordingStatus>();

        group.MapPost("/background/relearn", (FinishRecordingService service) =>
        {
            service.RelearnBackground();
            return Results.Ok(service.GetStatus());
        }).Produces<FinishRecordingStatus>();

        // External programs only (not offered in the UI, FS1-60): pending recordings are saved before the process ends.
        // Outside the group: policies of group and endpoint would combine, and the operator cookie would satisfy them.
        app.MapPost("/api/control/shutdown", (HttpContext context, IHostApplicationLifetime lifetime, ILogger<StartRequest> logger) =>
        {
            logger.LogInformation("Shutdown requested by an external program");
            // After the response: the caller gets its answer before the host starts shutting down.
            context.Response.OnCompleted(() =>
            {
                lifetime.StopApplication();
                return Task.CompletedTask;
            });
            return Results.Accepted();
        }).WithTags("Control").RequireAuthorization(AccessPolicies.External).Produces(StatusCodes.Status202Accepted);
    }
}
