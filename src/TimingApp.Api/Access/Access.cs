using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TimingApp.Api.Access;

/// <summary>Operator access by PIN (<c>TimingApp:Access</c>). The PIN must be configured outside the repository in production.</summary>
public sealed class AccessOptions
{
    public const string Section = "TimingApp:Access";

    public string? OperatorPin { get; set; }

    public bool Matches(string? pin) => Secrets.Matches(OperatorPin, pin);
}

/// <summary>
/// Machine-to-machine access of external programs (FS1-50, FS1-51, OP-1): a shared key in the <c>X-Api-Key</c>
/// header (<c>TimingApp:ExternalControl</c>). Without a configured key, external control is disabled.
/// </summary>
public sealed class ExternalControlOptions
{
    public const string Section = "TimingApp:ExternalControl";

    public string? ApiKey { get; set; }
}

/// <summary>Authentication schemes and authorization policies.</summary>
internal static class AccessPolicies
{
    public const string ApiKeyScheme = "ApiKey";
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>Operating commands: a signed-in operator or an authorized external program.</summary>
    public const string Control = "control";

    /// <summary>Only for external programs (shutting the application down).</summary>
    public const string External = "external";

    /// <summary>Role of a caller authenticated by API key; an operator cookie never carries it.</summary>
    public const string ExternalRole = "external";
}

internal static class Secrets
{
    /// <summary>Constant-time comparison; an unconfigured secret never matches.</summary>
    public static bool Matches(string? configured, string? given)
    {
        if (string.IsNullOrEmpty(configured) || given is null)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)),
            SHA256.HashData(Encoding.UTF8.GetBytes(given)));
    }
}

/// <summary>Authenticates external programs by the <c>X-Api-Key</c> header.</summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<ExternalControlOptions> external) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AccessPolicies.ApiKeyHeader, out var given))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        if (!Secrets.Matches(external.Value.ApiKey, given.ToString()))
        {
            Logger.LogWarning("Rejected external control request from {Address}", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "external"), new Claim(ClaimTypes.Role, AccessPolicies.ExternalRole)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
