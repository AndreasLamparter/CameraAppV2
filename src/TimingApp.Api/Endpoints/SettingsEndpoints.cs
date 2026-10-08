using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using TimingApp.Api.Access;
using TimingApp.Api.Hosting;
using TimingApp.Application.FinishRecording;

namespace TimingApp.Api.Endpoints;

/// <summary>
/// How external programs reach the control API (FS1-50, FS1-64): whether a key is configured, the key for the
/// signed-in operator to pass on (null when none is configured), the header carrying it and the base URLs of this
/// computer.
/// </summary>
public sealed record ExternalControlInfo(bool Enabled, string? ApiKey, string ApiKeyHeader, IReadOnlyList<string> BaseUrls);

/// <summary>Settings (FS1-64), the camera devices to choose from and how external programs reach the control API.</summary>
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

        group.MapGet("/settings/external-control", (HttpContext context, IServer server, IOptions<ExternalControlOptions> external) =>
        {
            // Contains the shared key: never cached by the browser or a proxy.
            context.Response.Headers.CacheControl = "no-store";
            var listening = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
            var key = string.IsNullOrEmpty(external.Value.ApiKey) ? null : external.Value.ApiKey;
            return Results.Ok(new ExternalControlInfo(
                key is not null,
                key,
                AccessPolicies.ApiKeyHeader,
                NetworkAddresses.BaseUrls(listening, NetworkAddresses.LocalIPv4())));
        }).Produces<ExternalControlInfo>();

        group.MapGet("/cameras/devices", async (ICameraSystem cameras, CancellationToken ct) => Results.Ok(await cameras.ListDevicesAsync(ct)))
            .Produces<IReadOnlyList<CameraDevice>>();
    }
}
