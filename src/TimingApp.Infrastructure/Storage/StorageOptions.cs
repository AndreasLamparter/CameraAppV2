using TimingApp.Application.FinishRecording;

namespace TimingApp.Infrastructure.Storage;

/// <summary>Data directory (<c>TimingApp:Storage</c>). Relative paths are resolved against the working directory.</summary>
public sealed class StorageOptions
{
    public const string Section = "TimingApp:Storage";

    public string DataDirectory { get; set; } = @"C:\ProgramData\TimingApp";

    public string FullDataDirectory => Path.GetFullPath(DataDirectory);

    public string SettingsFile => Path.Combine(FullDataDirectory, "settings.json");

    public string DefaultMediaDirectory => Path.Combine(FullDataDirectory, "media");
}

/// <summary>The configured media directory, or <c>&lt;DataDirectory&gt;/media</c> by default.</summary>
internal sealed class MediaDirectoryResolver(StorageOptions storage) : IMediaDirectoryResolver
{
    public string Resolve(string? configured) => string.IsNullOrWhiteSpace(configured) ? storage.DefaultMediaDirectory : Path.GetFullPath(configured);
}
