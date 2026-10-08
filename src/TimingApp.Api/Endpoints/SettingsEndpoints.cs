using TimingApp.Application.FinishRecording;

namespace TimingApp.Api.Endpoints;

/// <summary>Settings (FS1-64) and the camera devices to choose from.</summary>
internal static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Settings");

        group.MapGet("/settings", async (SettingsService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .Produces<SettingsResponse>();

        group.MapPut("/settings", async (SettingsDto settings, SettingsService service, CancellationToken ct) =>
            (await service.UpdateAsync(settings, ct)).ToHttp())
            .Produces<SettingsResponse>().ProducesErrors(400);

        group.MapGet("/cameras/devices", async (ICameraSystem cameras, CancellationToken ct) => Results.Ok(await cameras.ListDevicesAsync(ct)))
            .Produces<IReadOnlyList<CameraDevice>>();
    }
}
