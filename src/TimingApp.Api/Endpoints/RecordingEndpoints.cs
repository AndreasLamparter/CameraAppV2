using TimingApp.Application.FinishRecording;

namespace TimingApp.Api.Endpoints;

/// <summary>Recording list, playback data, media files (with range requests for video seeking) and deletion.</summary>
internal static class RecordingEndpoints
{
    public static void MapRecordingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recordings").WithTags("Recordings");

        group.MapGet("/", async (RecordingService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .Produces<IReadOnlyList<RecordingSummary>>();

        group.MapGet("/{id}", async (string id, RecordingService service, CancellationToken ct) =>
            (await service.GetAsync(id, ct)).ToHttp())
            .Produces<RecordingMetadata>().ProducesErrors(404);

        group.MapGet("/{id}/files/{file}", async (string id, RecordingFile file, bool? download, RecordingService service, CancellationToken ct) =>
        {
            var path = await service.GetFileAsync(id, file, ct);
            if (path.IsFailure)
            {
                return HttpResults.Problem(path.Error!);
            }
            var (contentType, extension) = file == RecordingFile.FinishImage ? ("image/png", "png") : ("video/mp4", "mp4");
            var name = download == true ? $"{id}-{file}.{extension}" : null;
            return Results.File(path.Value, contentType, name, enableRangeProcessing: true);
        })
        .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
        .ProducesErrors(404);

        group.MapDelete("/{id}", async (string id, RecordingService service, CancellationToken ct) =>
            (await service.DeleteAsync(id, ct)).ToHttp())
            .Produces(StatusCodes.Status204NoContent).ProducesErrors(404);
    }
}
