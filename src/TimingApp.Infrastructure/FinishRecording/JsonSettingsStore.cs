using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;
using TimingApp.Infrastructure.Storage;

namespace TimingApp.Infrastructure.FinishRecording;

/// <summary>Settings as a durable JSON file (<c>settings.json</c> in the data directory); defaults until first saved.</summary>
internal sealed class JsonSettingsStore(StorageOptions storage, ILogger<JsonSettingsStore> logger) : ISettingsStore, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private FinishRecordingSettings? _cached;

    public async Task<FinishRecordingSettings> GetAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _cached ??= await LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(FinishRecordingSettings settings, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(settings.ToDto(), StoreJson.Options);
            await DurableFile.WriteAllTextAsync(storage.SettingsFile, json, cancellationToken).ConfigureAwait(false);
            _cached = settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task<FinishRecordingSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await DurableFile.ReadAllTextAsync(storage.SettingsFile, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return FinishRecordingSettings.Default;
        }
        try
        {
            var settings = JsonSerializer.Deserialize<SettingsDto>(Migrate(json), StoreJson.Options)?.ToDomain();
            if (settings is not null && settings.Validate().IsSuccess)
            {
                return settings;
            }
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Settings file {Path} is not readable", storage.SettingsFile);
        }
        logger.LogError("Settings file {Path} is invalid; using the standard settings until saved again", storage.SettingsFile);
        return FinishRecordingSettings.Default;
    }

    /// <summary>
    /// Files written before the image rotation existed hold the line orientation instead: a horizontal line (pixel row)
    /// becomes the image turned clockwise, whose column is that row. Throws <see cref="JsonException"/> for invalid JSON.
    /// </summary>
    internal static string Migrate(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root ||
            root["finishLine"] is not JsonObject line ||
            line.ContainsKey("rotation") ||
            line["orientation"]?.GetValue<string>() is not { } orientation)
        {
            return json;
        }
        line.Remove("orientation");
        line["rotation"] = orientation == "Horizontal" ? "Clockwise90" : "None";
        if (orientation == "Horizontal" && root["finishCamera"] is JsonObject camera &&
            camera["height"]?.GetValue<int>() is { } height && line["position"]?.GetValue<int>() is { } position &&
            line["width"]?.GetValue<int>() is { } width)
        {
            // The same pixel row counted from the left edge of the turned image.
            line["position"] = height - position - width;
        }
        return root.ToJsonString();
    }
}
