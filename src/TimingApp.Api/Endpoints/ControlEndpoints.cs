using TimingApp.Api.Access;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Api.Endpoints;

/// <summary>Start or switch the mode; Recording needs the name of the race (FS2-01).</summary>
public sealed record StartRequest(OperatingMode Mode, string? RaceName = null);

/// <summary>A passage of the timing system (FS2-10): start number and passage time with time zone.</summary>
public sealed record PassageRequest(string? StartNumber, DateTimeOffset? Time);

public sealed record TriggerRequest(bool Active);

/// <summary>Operating commands and passages for the UI and for external programs (FS1-50, FS1-60, FS2-10).</summary>
internal static class ControlEndpoints
{
    public static void MapControlEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/control").WithTags("Control").RequireAuthorization(AccessPolicies.Control);

        group.MapGet("/status", (FinishRecordingService service) => Results.Ok(service.GetStatus()))
            .Produces<FinishRecordingStatus>();

        group.MapPost("/start", async (StartRequest request, FinishRecordingService service, CancellationToken ct) =>
        {
            var result = await service.StartAsync(request.Mode, request.RaceName, ct);
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

        // Passages (FS2-10, FS2-16): accepted and assigned to a recording asynchronously.
        app.MapPost("/api/passages", (PassageRequest request, FinishRecordingService service) =>
        {
            var result = service.ReportPassage(request.StartNumber, request.Time);
            return result.IsSuccess ? Results.Accepted(value: result.Value) : HttpResults.Problem(result.Error!);
        }).WithTags("Control").RequireAuthorization(AccessPolicies.Control).Produces<PassageReceipt>(StatusCodes.Status202Accepted).ProducesErrors(400, 409);

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
