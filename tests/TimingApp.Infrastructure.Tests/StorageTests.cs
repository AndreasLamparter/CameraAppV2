using Microsoft.Extensions.Logging.Abstractions;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;
using TimingApp.Infrastructure.FinishRecording;
using TimingApp.Infrastructure.Storage;

namespace TimingApp.Infrastructure.Tests;

public sealed class StorageTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private readonly StorageOptions _storage = new() { DataDirectory = Path.Combine(Path.GetTempPath(), "timingapp-tests", Guid.NewGuid().ToString("N")) };
    private readonly JsonSettingsStore _settings;
    private readonly FileRecordingStore _store;

    public StorageTests()
    {
        _settings = new JsonSettingsStore(_storage, NullLogger<JsonSettingsStore>.Instance);
        _store = new FileRecordingStore(_settings, new MediaDirectoryResolver(_storage), NullLogger<FileRecordingStore>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _settings.Dispose();
        if (Directory.Exists(_storage.DataDirectory))
        {
            Directory.Delete(_storage.DataDirectory, recursive: true);
        }
    }

    private static RecordingMetadata Metadata(RecordingTarget target) => new(
        target.Id.Value, Start, Start.AddSeconds(3), Start.AddSeconds(0.5), Start.AddSeconds(3), FinishEventEnd.PostRoll, 90, false,
        271, 1116, 36, [UnixMicroseconds.From(Start), UnixMicroseconds.From(Start.AddSeconds(3))],
        new VideoInfo(true, null), new FrontVideoInfo(true, null, 30, [1, 2]), 0, -40);

    private async Task<RecordingTarget> CompleteRecordingAsync()
    {
        var target = await _store.CreateAsync(Start, null, null, Ct);
        await File.WriteAllBytesAsync(target.FinishImagePath, new byte[100], Ct);
        await File.WriteAllBytesAsync(target.FrontVideoPath, new byte[50], Ct);
        await _store.CompleteAsync(target, Metadata(target), Ct);
        return target;
    }

    private static RaceName Race(string name) => RaceName.Create(name).Value;

    [Fact]
    public async Task Szenario_AufnahmenWerdenJeRennenAbgelegt()
    {
        var withoutRace = await CompleteRecordingAsync();
        var target = await _store.CreateAsync(Start, null, Race("Lauf 1"), Ct);
        await _store.CompleteAsync(target, Metadata(target) with { RaceName = "Lauf 1", Passages = [new PassageInfo("42", 1)] }, Ct);

        Assert.Equal(Path.Combine(_storage.FullDataDirectory, "media", "Lauf 1", target.Id.Value), target.Directory);
        Assert.NotEqual(withoutRace.Id, target.Id);
        var listed = (await _store.ListAsync(Ct)).Single(r => r.Id == target.Id.Value);
        Assert.Equal("Lauf 1", listed.RaceName);
        Assert.Equal(["42"], listed.StartNumbers);
        Assert.Equal(2, (await _store.ListAsync(Ct)).Count);
        Assert.Equal("Lauf 1", (await _store.GetAsync(target.Id, Ct)).Value.RaceName);
        Assert.True((await _store.GetFileAsync(target.Id, RecordingFile.FinishVideo, Ct)).IsFailure);
    }

    [Fact]
    public async Task RecordingIds_AreUniqueAcrossRaces()
    {
        var first = await _store.CreateAsync(Start, null, Race("Lauf 1"), Ct);
        var second = await _store.CreateAsync(Start, null, Race("Lauf 2"), Ct);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task UpdatePassages_OfARecordingInARace_RewritesTheMetadata()
    {
        var target = await _store.CreateAsync(Start, null, Race("Lauf 1"), Ct);
        await _store.CompleteAsync(target, Metadata(target) with { RaceName = "Lauf 1" }, Ct);

        var result = await _store.UpdatePassagesAsync(target.Id, [new PassageInfo("7", 5), new PassageInfo("8", 6)], Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["7", "8"], (await _store.GetAsync(target.Id, Ct)).Value.Passages!.Select(p => p.StartNumber));
        Assert.Equal(["7", "8"], Assert.Single(await _store.ListAsync(Ct)).StartNumbers);
    }

    [Fact]
    public async Task DeleteAsync_RecordingInARace_RemovesIt()
    {
        var target = await _store.CreateAsync(Start, null, Race("Lauf 1"), Ct);
        await _store.CompleteAsync(target, Metadata(target), Ct);

        Assert.True((await _store.DeleteAsync(target.Id, Ct)).IsSuccess);
        Assert.Empty(await _store.ListAsync(Ct));
    }

    [Fact]
    public async Task Szenario_UnvollstaendigeAufnahmeWirdNichtAngezeigt()
    {
        var target = await _store.CreateAsync(Start, null, null, Ct);
        await File.WriteAllBytesAsync(target.FinishImagePath, new byte[100], Ct);

        Assert.Empty(await _store.ListAsync(Ct));
        Assert.Equal("recording.notFound", (await _store.GetFileAsync(target.Id, RecordingFile.FinishImage, Ct)).Error?.Code);

        await _store.CompleteAsync(target, Metadata(target), Ct);
        var listed = Assert.Single(await _store.ListAsync(Ct));
        Assert.Equal(target.Id.Value, listed.Id);
    }

    [Fact]
    public async Task ListAsync_CompleteRecording_ReportsDurationFrontVideoAndSize()
    {
        await CompleteRecordingAsync();

        var summary = Assert.Single(await _store.ListAsync(Ct));

        Assert.Equal(3, summary.DurationSeconds);
        Assert.Equal(90, summary.LineRate);
        Assert.True(summary.HasFrontVideo);
        Assert.False(summary.HasVideoError);
        Assert.True(summary.SizeBytes > 150);
    }

    [Fact]
    public async Task CreateAsync_SameStart_GetsUniqueIds()
    {
        var first = await _store.CreateAsync(Start, null, null, Ct);
        var second = await _store.CreateAsync(Start, null, null, Ct);

        Assert.NotEqual(first.Id, second.Id);
        Assert.StartsWith(Path.Combine(_storage.FullDataDirectory, "media"), first.Directory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetFileAsync_ExistingAndMissingFiles()
    {
        var target = await CompleteRecordingAsync();

        Assert.Equal(target.FinishImagePath, (await _store.GetFileAsync(target.Id, RecordingFile.FinishImage, Ct)).Value);
        Assert.Equal("recording.fileNotFound", (await _store.GetFileAsync(target.Id, RecordingFile.FinishVideo, Ct)).Error?.Code);
    }

    [Fact]
    public async Task Szenario_AufnahmeLoeschen_RemovesFilesAndEntry()
    {
        var target = await CompleteRecordingAsync();

        var result = await _store.DeleteAsync(target.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(await _store.ListAsync(Ct));
        Assert.False(Directory.Exists(target.Directory));
        Assert.Equal("recording.notFound", (await _store.DeleteAsync(target.Id, Ct)).Error?.Code);
    }

    [Fact]
    public async Task GetAsync_RoundTripsMetadataWithTimestamps()
    {
        var target = await CompleteRecordingAsync();

        var metadata = (await _store.GetAsync(target.Id, Ct)).Value;

        Assert.Equal(Metadata(target).ColumnTimestamps, metadata.ColumnTimestamps);
        Assert.Equal(-40, metadata.FrontOffsetMs);
        Assert.Equal(FinishEventEnd.PostRoll, metadata.EndReason);
    }

    [Fact]
    public async Task ConfiguredMediaDirectory_IsUsedForListing()
    {
        var media = Path.Combine(_storage.FullDataDirectory, "elsewhere");
        await _settings.SaveAsync(FinishRecordingSettings.Default with { MediaDirectory = media }, Ct);

        var target = await _store.CreateAsync(Start, media, null, Ct);
        await _store.CompleteAsync(target, Metadata(target), Ct);

        Assert.StartsWith(media, target.Directory, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await _store.ListAsync(Ct));
    }

    [Fact]
    public async Task SettingsStore_WithoutFile_ReturnsDefaults_AndPersistsChanges()
    {
        Assert.Equal(FinishRecordingSettings.Default, await _settings.GetAsync(Ct));

        var changed = FinishRecordingSettings.Default with { Detection = FinishRecordingSettings.Default.Detection with { PostRoll = TimeSpan.FromSeconds(2) } };
        await _settings.SaveAsync(changed, Ct);
        using var reopened = new JsonSettingsStore(_storage, NullLogger<JsonSettingsStore>.Instance);

        Assert.Equal(changed, await reopened.GetAsync(Ct));
    }

    [Theory]
    [InlineData("Vertical", 300, "None", 300)]
    [InlineData("Horizontal", 300, "Clockwise90", 1080 - 300 - 2)]
    public async Task SettingsStore_FileWithLineOrientation_IsMigratedToImageRotation(string orientation, int position, string rotation, int migratedPosition)
    {
        var dto = FinishRecordingSettings.Default.ToDto();
        var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(dto, StoreJson.Options))!;
        var line = json["finishLine"]!.AsObject();
        line.Remove("rotation");
        line["orientation"] = orientation;
        line["position"] = position;
        line["width"] = 2;
        Directory.CreateDirectory(_storage.FullDataDirectory);
        await File.WriteAllTextAsync(_storage.SettingsFile, json.ToJsonString(), Ct);

        var settings = await _settings.GetAsync(Ct);

        Assert.Equal(Enum.Parse<ImageRotation>(rotation), settings.FinishLine.Rotation);
        Assert.Equal(migratedPosition, settings.FinishLine.Position);
    }

    [Fact]
    public async Task SettingsStore_CorruptFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_storage.FullDataDirectory);
        await File.WriteAllTextAsync(_storage.SettingsFile, "{ not json", Ct);

        Assert.Equal(FinishRecordingSettings.Default, await _settings.GetAsync(Ct));
    }
}
