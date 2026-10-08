using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TimingApp.Application.FinishRecording;
using TimingApp.Domain.FinishRecording;

namespace TimingApp.Api.Tests;

public sealed class ApiTests : IAsyncLifetime
{
    private readonly TimingAppFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        _factory.DeleteData();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(TimingAppFactory.Json, Ct))!;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> probe, Func<T, bool> done, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            var value = await probe();
            if (done(value) || DateTime.UtcNow > deadline)
            {
                return value;
            }
            await Task.Delay(100, Ct);
        }
    }

    [Fact]
    public async Task OpenApi_CheckedInContract_MatchesGeneratedDocument()
    {
        var generated = await _factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);
        var path = Path.Combine(RepositoryRoot(), "openapi", "openapi.json");
        var checkedIn = File.Exists(path) ? await File.ReadAllTextAsync(path, Ct) : "{}";

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(generated), JsonNode.Parse(checkedIn)),
            "openapi/openapi.json is outdated – run scripts\\generate-api.ps1");
    }

    [Theory]
    [InlineData("/api/recordings")]
    [InlineData("/api/settings")]
    [InlineData("/api/control/status")]
    [InlineData("/api/cameras/devices")]
    [InlineData("/api/live/FinishCamera")]
    public async Task Endpoint_WithoutLogin_Returns401(string url)
    {
        var response = await _factory.CreateClient().GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPin_Returns401WithStableCode()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { pin = "0000" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("access.wrongPin", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        var response = await _factory.CreateClient().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Szenario_ExterneSteuerung()
    {
        var external = _factory.CreateExternalClient();

        await ReadAsync<FinishRecordingStatus>(await external.PostAsJsonAsync("/api/control/start", new { mode = "Recording" }, Ct));
        var status = await ReadAsync<FinishRecordingStatus>(await external.GetAsync("/api/control/status", Ct));

        Assert.Equal(OperatingMode.Recording, status.Mode);
        await ReadAsync<FinishRecordingStatus>(await external.PutAsJsonAsync("/api/control/trigger", new { active = true }, Ct));
        Assert.True((await ReadAsync<FinishRecordingStatus>(await external.GetAsync("/api/control/status", Ct))).Line.ManualTrigger);
        await ReadAsync<FinishRecordingStatus>(await external.PutAsJsonAsync("/api/control/trigger", new { active = false }, Ct));
        await ReadAsync<FinishRecordingStatus>(await external.PostAsync("/api/control/background/relearn", null, Ct));
        var stopped = await ReadAsync<FinishRecordingStatus>(await external.PostAsync("/api/control/stop", null, Ct));
        Assert.Equal(OperatingMode.Stopped, stopped.Mode);
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData(null)]
    public async Task Szenario_ExterneSteuerungOhneBerechtigung(string? apiKey)
    {
        var external = _factory.CreateExternalClient(apiKey);

        var response = await external.PostAsJsonAsync("/api/control/start", new { mode = "Recording" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var status = await ReadAsync<FinishRecordingStatus>(await _factory.CreateExternalClient().GetAsync("/api/control/status", Ct));
        Assert.Equal(OperatingMode.Stopped, status.Mode);
    }

    [Fact]
    public async Task ExternalControl_NoKeyConfigured_IsDisabled()
    {
        await using var factory = new TimingAppFactory(externalControlEnabled: false);

        var response = await factory.CreateExternalClient(string.Empty).GetAsync("/api/control/status", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.DeleteData();
    }

    [Fact]
    public async Task Shutdown_ByOperator_IsForbidden_OnlyExternalProgramsMayShutDown()
    {
        var operatorClient = await _factory.LoginAsync();

        var response = await operatorClient.PostAsync("/api/control/shutdown", null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Shutdown_ByExternalProgram_StopsTheApplication()
    {
        var lifetime = _factory.Services.GetRequiredService<IHostApplicationLifetime>();

        var response = await _factory.CreateExternalClient().PostAsync("/api/control/shutdown", null, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await EventuallyAsync(() => Task.FromResult(lifetime.ApplicationStopping.IsCancellationRequested), stopping => stopping, seconds: 5);
        Assert.True(lifetime.ApplicationStopping.IsCancellationRequested);
    }

    [Fact]
    public async Task ApiKey_DoesNotGrantAccessToOperatorEndpoints()
    {
        var response = await _factory.CreateExternalClient().GetAsync("/api/recordings", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Settings_InvalidValue_Returns400WithStableCode()
    {
        var client = await _factory.LoginAsync();
        var current = await ReadAsync<SettingsResponse>(await client.GetAsync("/api/settings", Ct));

        var response = await client.PutAsJsonAsync("/api/settings", current.Settings with { Detection = current.Settings.Detection with { PixelThreshold = 0 } }, TimingAppFactory.Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("settings.pixelThresholdInvalid", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Recording_ThroughSimulatedCameras_IsListedPlayableAndDeletable()
    {
        var client = await _factory.LoginAsync();
        var settings = (await ReadAsync<SettingsResponse>(await client.GetAsync("/api/settings", Ct))).Settings;
        Assert.False((await ReadAsync<SettingsResponse>(await client.PutAsJsonAsync("/api/settings", settings with
        {
            FinishCamera = settings.FinishCamera with { Width = 640, Height = 480, FrameRate = 60 },
            FrontCamera = settings.FrontCamera with { Width = 320, Height = 240 },
            FinishLine = settings.FinishLine with { Position = 320 },
            Detection = settings.Detection with { PostRollSeconds = 0.5, FrontPreRollSeconds = 0.5, FrontPostRollSeconds = 0.5 },
        }, TimingAppFactory.Json, Ct))).AppliesOnNextStart);

        await ReadAsync<FinishRecordingStatus>(await client.PostAsJsonAsync("/api/control/start", new { mode = "Recording" }, Ct));
        await EventuallyAsync(() => ReadAsync<FinishRecordingStatus>(client.GetAsync("/api/control/status", Ct).Result), s => !s.Line.BackgroundLearning && s.FinishCamera.State == CameraState.Running);
        (await client.PutAsJsonAsync("/api/simulator/occupancy", new { occupied = true }, Ct)).EnsureSuccessStatusCode();
        await Task.Delay(500, Ct);
        (await client.PutAsJsonAsync("/api/simulator/occupancy", new { occupied = false }, Ct)).EnsureSuccessStatusCode();

        var list = await EventuallyAsync(async () => await ReadAsync<List<RecordingSummary>>(await client.GetAsync("/api/recordings", Ct)), l => l.Count > 0);
        var summary = Assert.Single(list);
        Assert.True(summary.HasVideoError);
        Assert.Equal("video.ffmpegMissing", (await ReadAsync<FinishRecordingStatus>(await client.GetAsync("/api/control/status", Ct))).LastProblem?.Code);

        var metadata = await ReadAsync<RecordingMetadata>(await client.GetAsync($"/api/recordings/{summary.Id}", Ct));
        Assert.Equal(metadata.ColumnTimestamps.Count, metadata.ImageWidth);
        Assert.Equal(480 + 36, metadata.ImageHeight);

        using var range = new HttpRequestMessage(HttpMethod.Get, $"/api/recordings/{summary.Id}/files/FinishImage");
        range.Headers.Range = new RangeHeaderValue(0, 7);
        var partial = await client.SendAsync(range, Ct);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("image/png", partial.Content.Headers.ContentType?.MediaType);
        Assert.Equal(8, (await partial.Content.ReadAsByteArrayAsync(Ct)).Length);

        var download = await client.GetAsync($"/api/recordings/{summary.Id}/files/FinishImage?download=true", Ct);
        Assert.Equal($"{summary.Id}-FinishImage.png", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/recordings/{summary.Id}/files/FinishVideo", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/recordings/{summary.Id}", Ct)).StatusCode);
        Assert.Empty(await ReadAsync<List<RecordingSummary>>(await client.GetAsync("/api/recordings", Ct)));
        await client.PostAsync("/api/control/stop", null, Ct);
    }

    [Theory]
    [InlineData("/api/recordings/..%2F..%2Fsettings")]
    [InlineData("/api/recordings/..%5Csettings/files/FinishImage")]
    [InlineData("/api/recordings/20261007-100000-000")]
    public async Task Recording_InvalidOrUnknownId_Returns404(string url)
    {
        var client = await _factory.LoginAsync();

        var response = await client.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LivePreview_WhileRunning_StreamsMjpeg()
    {
        var client = await _factory.LoginAsync();
        await ReadAsync<FinishRecordingStatus>(await client.PostAsJsonAsync("/api/control/start", new { mode = "Preview" }, Ct));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync("/api/live/FinishCamera", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal("multipart/x-mixed-replace", response.Content.Headers.ContentType?.MediaType);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var buffer = new byte[64];
        var read = await stream.ReadAtLeastAsync(buffer, 16, throwOnEndOfStream: false, timeout.Token);

        Assert.StartsWith("--frame", System.Text.Encoding.ASCII.GetString(buffer, 0, read), StringComparison.Ordinal);
        await client.PostAsync("/api/control/stop", null, Ct);
    }

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TimingApp.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
