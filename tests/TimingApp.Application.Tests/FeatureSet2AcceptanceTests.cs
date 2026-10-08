using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Application.Tests;

/// <summary>Acceptance criteria of FEATURE-SET-2 (races and start numbers) on the application with fake ports.</summary>
public sealed class FeatureSet2AcceptanceTests : IAsyncLifetime
{
    private readonly FinishRecordingHarness _app = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private async Task StartRecordingAsync(string race = "Lauf 1")
    {
        Assert.True((await _app.Service.StartAsync(OperatingMode.Recording, race, Ct)).IsSuccess);
        await _app.Session.FeedAsync(Line.Columns(-5, -3, Line.Free));
    }

    private static DateTimeOffset At(double seconds) => Line.TenOClock.AddSeconds(seconds);

    private async Task<RecordingMetadata> WaitForPassagesAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            if (_app.Store.Complete.FirstOrDefault(r => r.Passages?.Count >= count) is { } recording)
            {
                return recording;
            }
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No recording with {count} passages.");
            }
            await Task.Delay(10, Ct);
        }
    }

    [Theory]
    [InlineData(null, "race.nameRequired")]
    [InlineData("", "race.nameRequired")]
    [InlineData("   ", "race.nameRequired")]
    [InlineData("../Lauf", "race.nameInvalid")]
    public async Task Szenario_AufnahmeOhneRennname(string? race, string code)
    {
        var result = await _app.Service.StartAsync(OperatingMode.Recording, race, Ct);

        Assert.Equal(code, result.Error?.Code);
        Assert.Equal(OperatingMode.Stopped, _app.Service.GetStatus().Mode);
        Assert.Empty(_app.Cameras.Starts);
    }

    [Fact]
    public async Task Szenario_AufnahmenWerdenJeRennenAbgelegt()
    {
        await StartRecordingAsync("Lauf 1");

        await _app.Session.FeedAsync(Line.Columns(-3, 6, Line.Between(0, 2)));
        await _app.WaitForRecordingsAsync(1);

        Assert.Equal("Lauf 1", Assert.Single(_app.Store.Complete).RaceName);
        Assert.StartsWith("media/Lauf 1/", Assert.Single(_app.Store.Created).Directory, StringComparison.Ordinal);
        Assert.Equal("Lauf 1", _app.Service.GetStatus().RaceName);
    }

    [Fact]
    public async Task Szenario_RennenWechseln()
    {
        await StartRecordingAsync("Lauf 1");

        Assert.True((await _app.Service.StartAsync(OperatingMode.Recording, "Lauf 2", Ct)).IsSuccess);
        await _app.Session.FeedAsync(Line.Columns(-3, 6, Line.Between(0, 2)));
        await _app.WaitForRecordingsAsync(1);

        Assert.Single(_app.Cameras.Starts);
        Assert.Equal("Lauf 2", Assert.Single(_app.Store.Complete).RaceName);
    }

    [Fact]
    public async Task Szenario_PassageWaehrendEinerAufnahme()
    {
        await StartRecordingAsync();
        await _app.Session.FeedAsync(Line.Columns(-3, 1.5, Line.Between(0, 2)));

        var receipt = _app.Service.ReportPassage("42", At(1));
        await _app.Session.FeedAsync(Line.Columns(1.5, 6, Line.Between(0, 2)));
        await _app.WaitForRecordingsAsync(1);

        Assert.True(receipt.IsSuccess);
        var recording = Assert.Single(_app.Store.Complete);
        Assert.Equal([new PassageInfo("42", UnixMicroseconds.From(At(1)))], recording.Passages);
    }

    [Fact]
    public async Task Passage_ArrivingAfterTheRecordingWasSaved_IsAddedToIt()
    {
        await StartRecordingAsync();
        await _app.Session.FeedAsync(Line.Columns(-3, 4, Line.Between(0, 1)));
        await _app.WaitForRecordingsAsync(1);
        Assert.Empty(Assert.Single(_app.Store.Complete).Passages!);

        // Reported 3.5 s after the passage at 0.5 s, within the 5 s of FS2-17.
        _app.Service.ReportPassage("7", At(0.5));
        await _app.Session.FeedAsync(Line.Columns(4, 4.1, Line.Free));

        var recording = await WaitForPassagesAsync(1);
        Assert.Equal("7", Assert.Single(recording.Passages!).StartNumber);
        Assert.Single(_app.Store.Complete);
    }

    [Fact]
    public async Task Szenario_PassageOhneErkanntesZielereignis()
    {
        await StartRecordingAsync();
        await _app.Session.FeedAsync(Line.Columns(-3, 5, Line.Free));

        _app.Service.ReportPassage("7", At(4));
        await _app.Session.FeedAsync(Line.Columns(5, 7, Line.Free));
        await _app.WaitForRecordingsAsync(1);

        var recording = Assert.Single(_app.Store.Complete);
        Assert.Equal(FinishEventEnd.Passage, recording.EndReason);
        Assert.True(recording.StartedAt <= At(3.5));
        Assert.True(recording.EndedAt >= At(5));
        Assert.Equal("7", Assert.Single(recording.Passages!).StartNumber);
    }

    [Fact]
    public async Task Passages_CloseTogetherWithoutEvent_ShareOneRecording()
    {
        await StartRecordingAsync();
        await _app.Session.FeedAsync(Line.Columns(-3, 5, Line.Free));

        _app.Service.ReportPassage("7", At(3));
        _app.Service.ReportPassage("8", At(3.4));
        await _app.Session.FeedAsync(Line.Columns(5, 7, Line.Free));
        await _app.WaitForRecordingsAsync(1);

        Assert.Equal(["7", "8"], Assert.Single(_app.Store.Complete).Passages!.Select(p => p.StartNumber));
    }

    [Fact]
    public async Task Szenario_PassageInDerVorschau()
    {
        await _app.Service.StartAsync(OperatingMode.Preview, null, Ct);

        var result = _app.Service.ReportPassage("7", At(0));

        Assert.Equal("passage.notRecording", result.Error?.Code);
    }

    [Fact]
    public async Task Szenario_VersatzDerZeitmessung()
    {
        _app.Settings.Settings = FinishRecordingSettings.Default with { PassageOffsetMs = 200 };
        await StartRecordingAsync();

        var receipt = _app.Service.ReportPassage("42", At(2));

        Assert.Equal(At(2.2), receipt.Value.Time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("4 2")]
    [InlineData("123456789012345678901")]
    public async Task Passage_InvalidStartNumber_IsRejected(string startNumber)
    {
        await StartRecordingAsync();

        Assert.Equal("passage.startNumberInvalid", _app.Service.ReportPassage(startNumber, At(0)).Error?.Code);
    }

    [Fact]
    public async Task SameStartNumber_ReportedTwice_CountsAsTwoPassages()
    {
        await StartRecordingAsync();
        await _app.Session.FeedAsync(Line.Columns(-3, 1, Line.Between(0, 3)));

        _app.Service.ReportPassage("5", At(0.5));
        _app.Service.ReportPassage("5", At(0.9));
        await _app.Session.FeedAsync(Line.Columns(1, 6, Line.Between(0, 3)));
        await _app.WaitForRecordingsAsync(1);

        Assert.Equal(2, Assert.Single(_app.Store.Complete).Passages!.Count(p => p.StartNumber == "5"));
    }
}
