using Serilog;
using TimingApp.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddTimingApp();

var app = builder.Build();
app.UseTimingApp();

try
{
    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point; public for integration tests.</summary>
public partial class Program;
