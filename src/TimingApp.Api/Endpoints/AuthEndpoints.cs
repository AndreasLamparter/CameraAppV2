using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using TimingApp.Api.Access;
using TimingApp.Domain.SharedKernel;

namespace TimingApp.Api.Endpoints;

public sealed record LoginRequest(string? Pin);

public sealed record SessionDto(bool SignedIn);

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Access");

        // Anonymous: the login itself. Rate limited against PIN guessing.
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting(CompositionRoot.LoginRateLimit)
            .Produces<SessionDto>().ProducesErrors(401);
        group.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();
        group.MapGet("/me", () => Results.Ok(new SessionDto(true))).Produces<SessionDto>();
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext context, IOptions<AccessOptions> access, ILogger<LoginRequest> logger)
    {
        if (!access.Value.Matches(request.Pin))
        {
            logger.LogWarning("Failed login from {Address}", context.Connection.RemoteIpAddress);
            return HttpResults.Problem(Error.Unauthorized("access.wrongPin"));
        }
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "operator")], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        logger.LogInformation("Operator signed in from {Address}", context.Connection.RemoteIpAddress);
        return Results.Ok(new SessionDto(true));
    }
}
