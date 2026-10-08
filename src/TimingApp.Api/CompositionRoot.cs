using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Serilog;
using TimingApp.Api.Access;
using TimingApp.Api.Endpoints;
using TimingApp.Api.Hosting;
using TimingApp.Api.Live;
using TimingApp.Application;
using TimingApp.Application.FinishRecording;
using TimingApp.Infrastructure;
using TimingApp.Infrastructure.Camera;
using TimingApp.Infrastructure.Storage;

namespace TimingApp.Api;

internal static class CompositionRoot
{
    public const string LoginRateLimit = "login";

    public static void AddTimingApp(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        builder.Host.UseSerilog((context, provider, logger) =>
        {
            var storage = provider.GetRequiredService<StorageOptions>();
            logger.ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(storage.FullDataDirectory, "logs", "timingapp-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 60);
        });

        services.Configure<AccessOptions>(configuration.GetSection(AccessOptions.Section));
        services.Configure<ExternalControlOptions>(configuration.GetSection(ExternalControlOptions.Section));
        services.Configure<FinishRecordingOptions>(configuration.GetSection(FinishRecordingOptions.Section));
        var shutdownTimeout = configuration.GetSection(FinishRecordingOptions.Section).Get<FinishRecordingOptions>()?.ShutdownTimeout
            ?? new FinishRecordingOptions().ShutdownTimeout;
        services.Configure<HostOptions>(o => o.ShutdownTimeout = shutdownTimeout);

        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddCameraInfrastructure(configuration);

        services.AddSingleton<LiveStatusPublisher>();
        services.AddSingleton<IFinishRecordingNotifier>(p => p.GetRequiredService<LiveStatusPublisher>());
        services.AddHostedService(p => p.GetRequiredService<LiveStatusPublisher>());
        services.AddHostedService<FinishRecordingLifecycle>();

        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        services.AddProblemDetails();
        services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
        {
            // The contract must not depend on the host it was generated on.
            document.Servers?.Clear();
            return Task.CompletedTask;
        }));
        services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "TimingApp.Session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromHours(16);
                o.SlidingExpiration = true;
                // API: answer 401/403 instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AccessPolicies.ApiKeyScheme, null);

        services.AddAuthorizationBuilder()
            // Default deny: every endpoint needs a signed-in operator unless explicitly anonymous or otherwise authorized.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build())
            .AddPolicy(AccessPolicies.Control, p => p
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, AccessPolicies.ApiKeyScheme)
                .RequireAuthenticatedUser())
            .AddPolicy(AccessPolicies.External, p => p
                .AddAuthenticationSchemes(AccessPolicies.ApiKeyScheme)
                .RequireRole(AccessPolicies.ExternalRole));

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public static void UseTimingApp(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self' data:; connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            await next(context);
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapOpenApi().AllowAnonymous();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous().ExcludeFromDescription();
        app.MapHub<FinishHub>("/hubs/finish");
        app.MapAuthEndpoints();
        app.MapControlEndpoints();
        app.MapRecordingEndpoints();
        app.MapSettingsEndpoints();
        app.MapLiveEndpoints();
        // Vue client routes: the SPA shell is public, its data is not.
        // Not for API/hub paths and not for missing files (a missing asset must be a 404, not the HTML shell).
        app.MapFallbackToFile(@"{*path:regex(^(?!api/|hubs/)(?!.*\.[^/]+$).*$)}", "index.html").AllowAnonymous();
    }
}
