using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TimingApp.Api.Tests;

/// <summary>
/// Application under test with its own data directory, simulated cameras and an ffmpeg path that does not exist
/// (deterministic: videos fail with <c>video.ffmpegMissing</c>, independent of the PC the tests run on).
/// </summary>
public sealed class TimingAppFactory(bool externalControlEnabled = true) : WebApplicationFactory<Program>
{
    public const string Pin = "4711";
    public const string ApiKey = "test-api-key";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "timingapp-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("TimingApp:Storage:DataDirectory", DataDirectory);
        builder.UseSetting("TimingApp:Access:OperatorPin", Pin);
        builder.UseSetting("TimingApp:ExternalControl:ApiKey", externalControlEnabled ? ApiKey : string.Empty);
        builder.UseSetting("TimingApp:Camera:Simulation:Enabled", "true");
        builder.UseSetting("TimingApp:Camera:FfmpegPath", Path.Combine(DataDirectory, "no-ffmpeg.exe"));
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }

    public async Task<HttpClient> LoginAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { pin = Pin }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return client;
    }

    public HttpClient CreateExternalClient(string? apiKey = ApiKey)
    {
        var client = CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
        return client;
    }

    public void DeleteData()
    {
        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
