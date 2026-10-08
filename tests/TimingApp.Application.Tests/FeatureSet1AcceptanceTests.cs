using Microsoft.Extensions.Logging.Abstractions;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Application.Tests;

/// <summary>Gherkin scenarios of FEATURE-SET-1 at the application boundary, with fakes at the hardware and storage ports.</summary>
public sealed class FeatureSet1AcceptanceTests : IAsyncLifetime
{
    private readonly FinishRecordingHarness _app = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private async Task StartAsync(OperatingMode mode)
    {
        await _app.Service.StartAsync(mode, mode == OperatingMode.Recording ? "Lauf 1" : null, Ct);
        // Background learned on a free line before every scenario.
        await _app.Session.FeedAsync(Line.Columns(-5, -3, Line.Free));
    }

    [Fact]
    public async Task Szenario_AufnahmeStarten()
    {
        Assert.Equal(OperatingMode.Stopped, _app.Service.GetStatus().Mode);

        await _app.Service.StartAsync(OperatingMode.Recording, "Lauf 1", Ct);

        var status = _app.Service.GetStatus();
        Assert.Equal(OperatingMode.Recording, status.Mode);
        Assert.True(status.FinishCamera.MeasuredFrameRate > 0);
        Assert.True(status.FrontCamera.MeasuredFrameRate > 0);
    }

    [Fact]
    public async Task Szenario_VorschauSpeichertKeineAufnahmen()
    {
        await StartAsync(OperatingMode.Preview);

        await _app.Session.FeedAsync(Line.Columns(-3, 0.5, Line.Between(0, 1)));
        Assert.True(_app.Service.GetStatus().Line.EventRunning);
        await _app.Session.FeedAsync(Line.Columns(0.5, 5, Line.Between(0, 1)));

        Assert.Equal(1, _app.Service.GetStatus().Line.DetectedEvents);
        await _app.Saver.DrainAsync(Ct);
        Assert.Empty(_app.Store.Complete);
    }

    [Fact]
    public async Task Szenario_ZielereignisWirdMitVorUndNachlaufGespeichert()
    {
        await StartAsync(OperatingMode.Recording);

        await _app.Session.FeedAsync(Line.Columns(-3, 6, Line.Between(0, 2)));
        await _app.WaitForRecordingsAsync(1);

        var recording = Assert.Single(_app.Store.Complete);
        Assert.True(recording.StartedAt <= Line.TenOClock.AddSeconds(-0.5));
        Assert.True(recording.EndedAt >= Line.TenOClock.AddSeconds(3));
        Assert.Equal(recording.ColumnTimestamps.Count, recording.ImageWidth);
        Assert.Equal(UnixMicroseconds.From(recording.StartedAt), recording.ColumnTimestamps[0]);
    }

    [Fact]
    public async Task Szenario_FrontvideoDecktDenZeitraumDerAufnahmeAb()
    {
        await StartAsync(OperatingMode.Recording);
        for (var i = 0; i < 20 * 30; i++)
        {
            // Front frames at 30 per second, not aligned with the finish columns.
            _app.Session.Front!.Add(new EncodedFrame(Line.TenOClock.AddSeconds(-10 + 0.013 + (i / 30.0)), [0xFF, 0xD8]));
        }

        await _app.Session.FeedAsync(Line.Columns(-3, 6, Line.Between(0, 2)));
        await _app.WaitForRecordingsAsync(1);

        var front = Assert.Single(_app.Store.Complete).FrontVideo!;
        Assert.True(front.Available);
        Assert.True(UnixMicroseconds.ToTimestamp(front.FrameTimestamps[0]) <= Line.TenOClock.AddSeconds(-2.5));
        Assert.True(UnixMicroseconds.ToTimestamp(front.FrameTimestamps[^1]) >= Line.TenOClock.AddSeconds(5));
        Assert.Equal(30, front.FrameRate, 1);
    }

    [Fact]
    public async Task Szenario_MaximaleDauerEinesZielereignisses()
    {
        await StartAsync(OperatingMode.Recording);

        await _app.Session.FeedAsync(Line.Columns(-3, 95, Line.Between(0, 90)));
        await _app.WaitForRecordingsAsync(2);

        var recordings = _app.Store.Complete.OrderBy(r => r.StartedAt).ToList();
        Assert.Equal(2, recordings.Count);
        Assert.Equal(FinishEventEnd.MaxDuration, recordings[0].EndReason);
        Assert.Equal(recordings[0].EventEndedAt, recordings[1].EventStartedAt);
    }

    [Fact]
    public async Task Szenario_ManuellerAusloeser()
    {
        await StartAsync(OperatingMode.Recording);
        await _app.Session.FeedAsync(Line.Columns(-3, 0, Line.Free));

        _app.Service.SetManualTrigger(true);
        await _app.Session.FeedAsync(Line.Columns(0, 3, Line.Free));
        Assert.Equal(LineState.Recording, _app.Service.GetStatus().Line.State);
        _app.Service.SetManualTrigger(false);
        await _app.Session.FeedAsync(Line.Columns(3, 6, Line.Free));
        await _app.WaitForRecordingsAsync(1);

        var recording = Assert.Single(_app.Store.Complete);
        Assert.Equal(Line.TenOClock, recording.EventStartedAt);
    }

    [Fact]
    public async Task Szenario_StoppenWaehrendEinesZielereignisses()
    {
        await StartAsync(OperatingMode.Recording);
        await _app.Session.FeedAsync(Line.Columns(-3, 1, Line.Between(0, 100)));
        Assert.True(_app.Service.GetStatus().Line.EventRunning);

        await _app.Service.StopAsync(Ct);
        await _app.WaitForRecordingsAsync(1);

        var recording = Assert.Single(_app.Store.Complete);
        Assert.Equal(FinishEventEnd.Stopped, recording.EndReason);
        Assert.True(recording.StartedAt <= Line.TenOClock.AddSeconds(-0.5));
        Assert.Equal(OperatingMode.Stopped, _app.Service.GetStatus().Mode);
    }

    [Fact]
    public async Task Szenario_FrontkameraFehlt()
    {
        _app.Cameras.FrontHealth = new CameraHealth(CameraState.Error, "camera.openFailed", 0);
        await StartAsync(OperatingMode.Recording);

        Assert.Equal("camera.openFailed", _app.Service.GetStatus().FrontCamera.ErrorCode);
        await _app.Session.FeedAsync(Line.Columns(-3, 6, Line.Between(0, 2)));
        await _app.WaitForRecordingsAsync(1);

        var recording = Assert.Single(_app.Store.Complete);
        Assert.Null(recording.FrontVideo);
        Assert.Null(_app.Service.GetStatus().LastProblem);
    }

    [Fact]
    public async Task Szenario_ZuNiedrigeLinienrate()
    {
        await StartAsync(OperatingMode.Recording);

        await _app.Session.FeedAsync(Line.Columns(0, 3, Line.Free, rate: 80));

        var line = _app.Service.GetStatus().Line;
        Assert.Equal(80, line.LineRate!.Value, 0);
        Assert.True(line.LineRateWarning);
    }

    [Fact]
    public async Task Szenario_VideoKannNichtErzeugtWerden()
    {
        _app.Encoder.FailWith = "video.encodingFailed";
        await StartAsync(OperatingMode.Recording);

        await _app.Session.FeedAsync(Line.Columns(-3, 6, Line.Between(0, 2)));
        await _app.WaitForRecordingsAsync(1);

        var recording = Assert.Single(_app.Store.Complete);
        Assert.False(recording.FinishVideo.Available);
        Assert.Equal("video.encodingFailed", recording.FinishVideo.ErrorCode);
        Assert.NotEmpty(recording.ColumnTimestamps);
        Assert.Equal("video.encodingFailed", _app.Service.GetStatus().LastProblem?.Code);
    }

    [Fact]
    public async Task Szenario_EinstellungenWaehrendDesBetriebsAendern()
    {
        await StartAsync(OperatingMode.Recording);
        var settings = new SettingsService(_app.Settings, new FixedMediaDirectory(), _app.Service, NullLogger<SettingsService>.Instance);
        var dto = (await settings.GetAsync(Ct)).Settings;

        var response = await settings.UpdateAsync(dto with { Detection = dto.Detection with { PostRollSeconds = 3 } }, Ct);

        Assert.True(response.Value.AppliesOnNextStart);
        // The running session keeps its settings until the next start (FS1-44).
        await _app.Session.FeedAsync(Line.Columns(-3, 6, Line.Between(0, 2)));
        await _app.WaitForRecordingsAsync(1);
        Assert.Equal(Line.TenOClock.AddSeconds(3), Assert.Single(_app.Store.Complete).EventEndedAt);
    }

    [Fact]
    public async Task Szenario_AnwendungExternBeenden()
    {
        _app.Encoder.Gate = new TaskCompletionSource();
        await StartAsync(OperatingMode.Recording);
        await _app.Session.FeedAsync(Line.Columns(-3, 6, Line.Between(0, 2)));
        Assert.Equal(1, _app.Service.GetStatus().PendingSaves);

        var shutdown = _app.Service.ShutdownAsync(Ct);
        await Task.Delay(100, Ct);
        Assert.False(shutdown.IsCompleted);
        _app.Encoder.Gate.SetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Single(_app.Store.Complete);
        Assert.Equal(0, _app.Service.GetStatus().PendingSaves);
    }

    [Fact]
    public async Task Szenario_AufnahmeLoeschen()
    {
        await StartAsync(OperatingMode.Recording);
        await _app.Session.FeedAsync(Line.Columns(-3, 6, Line.Between(0, 2)));
        await _app.WaitForRecordingsAsync(1);
        var recordings = new RecordingService(_app.Store, _app.Notifier, NullLogger<RecordingService>.Instance);
        var id = Assert.Single(await recordings.ListAsync(Ct)).Id;

        var result = await recordings.DeleteAsync(id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(await recordings.ListAsync(Ct));
    }
}
