using Microsoft.Extensions.Logging;
using TimingApp.Domain.FinishRecording;
using TimingApp.Domain.SharedKernel;

namespace TimingApp.Application.FinishRecording;

/// <summary>Reads and changes the settings (FS1-64). Changes apply from the next camera start (FS1-44).</summary>
public sealed class SettingsService(ISettingsStore store, IMediaDirectoryResolver media, FinishRecordingService recording, ILogger<SettingsService> logger)
{
    public async Task<SettingsResponse> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await store.GetAsync(cancellationToken).ConfigureAwait(false);
        return Response(settings);
    }

    public async Task<Result<SettingsResponse>> UpdateAsync(SettingsDto dto, CancellationToken cancellationToken)
    {
        var settings = dto.ToDomain();
        var valid = settings.Validate();
        if (valid.IsFailure)
        {
            return valid.Error!;
        }
        await store.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Settings changed");
        return Response(settings);
    }

    private SettingsResponse Response(FinishRecordingSettings settings) =>
        new(settings.ToDto(), media.Resolve(settings.MediaDirectory), recording.Mode != OperatingMode.Stopped);
}

/// <summary>Resolves the configured media directory (null = default below the data directory).</summary>
public interface IMediaDirectoryResolver
{
    string Resolve(string? configured);
}

/// <summary>Recording list, details, files and deletion (FS1-61 to FS1-63).</summary>
public sealed class RecordingService(IRecordingStore store, IFinishRecordingNotifier notifier, ILogger<RecordingService> logger)
{
    public Task<IReadOnlyList<RecordingSummary>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

    public async Task<Result<RecordingMetadata>> GetAsync(string? id, CancellationToken cancellationToken)
    {
        var parsed = RecordingId.Parse(id);
        return parsed.IsFailure ? parsed.Error! : await store.GetAsync(parsed.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<string>> GetFileAsync(string? id, RecordingFile file, CancellationToken cancellationToken)
    {
        var parsed = RecordingId.Parse(id);
        return parsed.IsFailure ? parsed.Error! : await store.GetFileAsync(parsed.Value, file, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> DeleteAsync(string? id, CancellationToken cancellationToken)
    {
        var parsed = RecordingId.Parse(id);
        if (parsed.IsFailure)
        {
            return parsed.Error!;
        }
        var result = await store.DeleteAsync(parsed.Value, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            logger.LogInformation("Recording {RecordingId} deleted", parsed.Value);
            notifier.RecordingsChanged();
        }
        return result;
    }
}
