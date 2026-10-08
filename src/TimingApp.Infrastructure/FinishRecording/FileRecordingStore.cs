using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;
using TimingApp.Domain.SharedKernel;
using TimingApp.Infrastructure.Storage;

namespace TimingApp.Infrastructure.FinishRecording;

internal static class StoreJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>
/// Recordings on the file system: one directory per recording below the media directory. <c>recording.json</c> is
/// written last through <see cref="DurableFile"/>; a directory without it is incomplete and never listed (FS1-42).
/// </summary>
internal sealed class FileRecordingStore(
    ISettingsStore settings,
    IMediaDirectoryResolver media,
    ILogger<FileRecordingStore> logger) : IRecordingStore
{
    public const string MetadataFile = "recording.json";
    public const string FinishImageFile = "finish.png";
    public const string FinishVideoFile = "finish.mp4";
    public const string FrontVideoFile = "front.mp4";

    private readonly ConcurrentDictionary<string, (DateTime Written, RecordingSummary Summary)> _summaries = new();

    public Task<RecordingTarget> CreateAsync(DateTimeOffset start, string? mediaDirectory, CancellationToken cancellationToken)
    {
        var root = media.Resolve(mediaDirectory);
        Directory.CreateDirectory(root);
        for (var suffix = 0; ; suffix++)
        {
            var id = RecordingId.For(start, suffix);
            var directory = Path.Combine(root, id.Value);
            if (Directory.Exists(directory))
            {
                continue;
            }
            Directory.CreateDirectory(directory);
            return Task.FromResult(new RecordingTarget(
                id,
                directory,
                Path.Combine(directory, FinishImageFile),
                Path.Combine(directory, FinishVideoFile),
                Path.Combine(directory, FrontVideoFile)));
        }
    }

    public Task CompleteAsync(RecordingTarget target, RecordingMetadata metadata, CancellationToken cancellationToken) =>
        DurableFile.WriteAllTextAsync(Path.Combine(target.Directory, MetadataFile), JsonSerializer.Serialize(metadata, StoreJson.Options), cancellationToken);

    public Task AbandonAsync(RecordingTarget target, CancellationToken cancellationToken)
    {
        TryDeleteDirectory(target.Directory);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<RecordingSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var root = await RootAsync(cancellationToken).ConfigureAwait(false);
        if (!Directory.Exists(root))
        {
            return [];
        }
        var result = new List<RecordingSummary>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var id = RecordingId.Parse(Path.GetFileName(directory));
            var metadataPath = Path.Combine(directory, MetadataFile);
            if (id.IsFailure || !File.Exists(metadataPath))
            {
                continue;
            }
            if (await SummaryAsync(directory, metadataPath, cancellationToken).ConfigureAwait(false) is { } summary)
            {
                result.Add(summary);
            }
        }
        return result.OrderByDescending(r => r.StartedAt).ToList();
    }

    public async Task<Result<RecordingMetadata>> GetAsync(RecordingId id, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(await RootAsync(cancellationToken).ConfigureAwait(false), id.Value);
        var metadata = await ReadMetadataAsync(Path.Combine(directory, MetadataFile), cancellationToken).ConfigureAwait(false);
        return metadata is null ? Error.NotFound("recording.notFound") : metadata;
    }

    public async Task<Result<string>> GetFileAsync(RecordingId id, RecordingFile file, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(await RootAsync(cancellationToken).ConfigureAwait(false), id.Value);
        if (!File.Exists(Path.Combine(directory, MetadataFile)))
        {
            return Error.NotFound("recording.notFound");
        }
        var path = Path.Combine(directory, file switch
        {
            RecordingFile.FinishImage => FinishImageFile,
            RecordingFile.FinishVideo => FinishVideoFile,
            RecordingFile.FrontVideo => FrontVideoFile,
            _ => throw new ArgumentOutOfRangeException(nameof(file)),
        });
        return File.Exists(path) ? path : Error.NotFound("recording.fileNotFound");
    }

    public async Task<Result> DeleteAsync(RecordingId id, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(await RootAsync(cancellationToken).ConfigureAwait(false), id.Value);
        var metadataPath = Path.Combine(directory, MetadataFile);
        if (!File.Exists(metadataPath))
        {
            return Error.NotFound("recording.notFound");
        }
        // Metadata first: a partly deleted recording is incomplete and no longer listed.
        File.Delete(metadataPath);
        _summaries.TryRemove(directory, out _);
        TryDeleteDirectory(directory);
        return Result.Success();
    }

    private async Task<string> RootAsync(CancellationToken cancellationToken) =>
        media.Resolve((await settings.GetAsync(cancellationToken).ConfigureAwait(false)).MediaDirectory);

    private async Task<RecordingSummary?> SummaryAsync(string directory, string metadataPath, CancellationToken cancellationToken)
    {
        var written = File.GetLastWriteTimeUtc(metadataPath);
        if (_summaries.TryGetValue(directory, out var cached) && cached.Written == written)
        {
            return cached.Summary;
        }
        var metadata = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            return null;
        }
        var size = new DirectoryInfo(directory).EnumerateFiles().Sum(f => f.Length);
        var summary = new RecordingSummary(
            metadata.Id,
            metadata.StartedAt,
            (metadata.EndedAt - metadata.StartedAt).TotalSeconds,
            metadata.LineRate,
            metadata.FrontVideo?.Available == true,
            !metadata.FinishVideo.Available || metadata.FrontVideo is { Available: false },
            size);
        _summaries[directory] = (written, summary);
        return summary;
    }

    private async Task<RecordingMetadata?> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        var json = await DurableFile.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<RecordingMetadata>(json, StoreJson.Options);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Recording metadata {Path} is not readable", path);
            return null;
        }
    }

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Directory {Directory} could not be removed completely", directory);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Directory {Directory} could not be removed completely", directory);
        }
    }
}
