using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Application.Tests;

public sealed class FinishRecordingServiceTests : IAsyncLifetime
{
    private readonly FinishRecordingHarness _app = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task StartAsync_WhileRunning_SwitchesModeWithoutRestartingCameras()
    {
        await _app.Service.StartAsync(OperatingMode.Preview, Ct);

        await _app.Service.StartAsync(OperatingMode.Recording, Ct);

        Assert.Single(_app.Cameras.Starts);
        Assert.Equal(OperatingMode.Recording, _app.Service.Mode);
    }

    [Fact]
    public async Task StartAsync_ModeStopped_IsRejected()
    {
        var result = await _app.Service.StartAsync(OperatingMode.Stopped, Ct);

        Assert.Equal("control.modeInvalid", result.Error?.Code);
    }

    [Fact]
    public async Task StartAsync_FrontCameraDisabled_StartsOnlyTheFinishCamera()
    {
        _app.Settings.Settings = FinishRecordingSettings.Default with { FrontCameraEnabled = false };

        await _app.Service.StartAsync(OperatingMode.Recording, Ct);

        Assert.Null(_app.Cameras.Starts[0].FrontCamera);
        Assert.Equal(CameraState.Disabled, _app.Service.GetStatus().FrontCamera.State);
    }

    [Fact]
    public async Task ModeSwitchToRecording_DuringPreviewEvent_SavesThatEvent()
    {
        await _app.Service.StartAsync(OperatingMode.Preview, Ct);
        await _app.Session.FeedAsync(Line.Columns(-5, 1, Line.Between(0, 2)));

        await _app.Service.StartAsync(OperatingMode.Recording, Ct);
        await _app.Session.FeedAsync(Line.Columns(1, 6, Line.Between(0, 2)));
        await _app.WaitForRecordingsAsync(1);

        Assert.Single(_app.Store.Complete);
    }

    [Fact]
    public async Task RelearnBackground_AfterLightChange_LineBecomesFreeAgain()
    {
        await _app.Service.StartAsync(OperatingMode.Preview, Ct);
        await _app.Session.FeedAsync(Line.Columns(-5, -3, Line.Free));
        var darker = Line.Columns(-3, 0, Line.Free).Select(c => c with { Bgr = c.Bgr.Select(b => (byte)(b / 2)).ToArray() }).ToList();
        await _app.Session.FeedAsync(darker.Take(10));
        Assert.Equal(LineState.Occupied, _app.Service.GetStatus().Line.State);

        _app.Service.RelearnBackground();
        await _app.Session.FeedAsync(darker.Skip(10).Take(FinishLineMonitor.LearningColumns + 5));

        var line = _app.Service.GetStatus().Line;
        Assert.False(line.BackgroundLearning);
        Assert.Equal(0, line.OccupancyPercent);
    }

    [Fact]
    public async Task Overlay_ReportsLinePositionAsFractionOfTheImage()
    {
        await _app.Service.StartAsync(OperatingMode.Preview, Ct);

        var overlay = _app.Service.GetStatus().Overlay!;

        Assert.Equal(0.5, overlay.Position);
    }

    [Fact]
    public void TimeRange_Covering_IncludesNeighboursOutsideTheRange()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var items = Enumerable.Range(0, 10).Select(i => t0.AddSeconds(i)).ToList();

        var covering = TimeRange.Covering(items, t => t, t0.AddSeconds(2.5), t0.AddSeconds(5.5));

        Assert.Equal([2, 3, 4, 5, 6], covering.Select(t => (int)(t - t0).TotalSeconds));
    }
}
