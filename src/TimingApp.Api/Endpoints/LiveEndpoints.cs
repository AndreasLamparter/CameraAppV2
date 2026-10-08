using System.Globalization;
using System.Text;
using TimingApp.Application.FinishRecording;
using TimingApp.Infrastructure.Camera;
using TimingApp.Infrastructure.Camera.Sources;
using TimingApp.Domain.SharedKernel;

namespace TimingApp.Api.Endpoints;

public sealed record SimulatorOccupancyRequest(bool Occupied);

/// <summary>Live previews as MJPEG (<c>multipart/x-mixed-replace</c>) and the control of the camera simulation.</summary>
internal static class LiveEndpoints
{
    private const string Boundary = "frame";

    public static void MapLiveEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/live/{kind}", StreamAsync).WithTags("Live")
            .Produces(StatusCodes.Status200OK, contentType: $"multipart/x-mixed-replace; boundary={Boundary}");

        // Simulation only (development, acceptance tests): a simulated rider on the finish line.
        app.MapPut("/api/simulator/occupancy", (SimulatorOccupancyRequest request, CameraOptions options, SimulationControl control) =>
        {
            if (!options.Simulation.Enabled)
            {
                return HttpResults.Problem(Error.Conflict("simulator.disabled"));
            }
            control.Occupied = request.Occupied;
            return Results.NoContent();
        }).WithTags("Simulator").Produces(StatusCodes.Status204NoContent).ProducesErrors(409);
    }

    private static async Task StreamAsync(PreviewKind kind, HttpContext context, ILivePreview preview, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(kind == PreviewKind.FinishStrip ? 200 : 100);
        context.Response.ContentType = $"multipart/x-mixed-replace; boundary={Boundary}";
        context.Response.Headers.CacheControl = "no-store";
        using var watch = preview.Watch(kind);
        byte[]? sent = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var jpeg = preview.Latest(kind);
                if (jpeg is not null && !ReferenceEquals(jpeg, sent))
                {
                    var header = Encoding.ASCII.GetBytes(
                        $"--{Boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
                    await context.Response.Body.WriteAsync(header, cancellationToken);
                    await context.Response.Body.WriteAsync(jpeg, cancellationToken);
                    await context.Response.Body.WriteAsync("\r\n"u8.ToArray(), cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                    sent = jpeg;
                }
                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client went away.
        }
    }
}
