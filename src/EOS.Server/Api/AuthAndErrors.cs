using System.Security.Claims;
using System.Text.Encodings.Web;
using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Hosting;
using EOS.Infrastructure.People;
using EOS.Infrastructure.Planning;
using EOS.Infrastructure.Security;
using EOS.Infrastructure.Skills;
using EOS.Infrastructure.Training;
using EOS.Server.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EOS.Server.Api;

public static class AuthExtensions
{
    public const string Scheme = "EosToken";
    public const string CookieName = "eos_token";

    public static IServiceCollection AddEosAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, TokenAuthHandler>(Scheme, _ => { });
        services.AddAuthorization(o => o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    public static string Actor(this HttpContext c) => c.User.Identity?.Name ?? "unknown";
}

public sealed class TokenAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, EosOptions eos, TokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = header["Bearer ".Length..].Trim();
        token ??= Request.Cookies[AuthExtensions.CookieName];

        if (token is not null)
        {
            var name = await tokens.ValidateAsync(token, Context.RequestAborted);
            if (name is not null) return Success(name);
            return AuthenticateResult.Fail("Invalid or revoked token.");
        }
        if (eos.DevNoAuth && System.Net.IPAddress.IsLoopback(Context.Connection.RemoteIpAddress ?? System.Net.IPAddress.None)) return Success("dev");
        return AuthenticateResult.NoResult();
    }

    private AuthenticateResult Success(string name)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], AuthExtensions.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), AuthExtensions.Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Request.Path.StartsWithSegments("/api") || Request.Path.StartsWithSegments("/mcp")) { Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; }
        Response.Redirect("/login?returnUrl=" + Uri.EscapeDataString(Request.Path + Request.QueryString));
        return Task.CompletedTask;
    }
}

public static class AuthApi
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/auth/login", async (HttpContext http, TokenService tokens, LoginRequest body) =>
        {
            var name = await tokens.ValidateAsync(body.Token);
            if (name is null) return Results.Json(new { error = "That token is not valid." }, statusCode: 401);
            Cookie(http, body.Token);
            return Results.Ok(new { name });
        }).AllowAnonymous().DisableAntiforgery();

        app.MapPost("/auth/logout", (HttpContext http) => { http.Response.Cookies.Delete(AuthExtensions.CookieName); return Results.Ok(); }).AllowAnonymous().DisableAntiforgery();

        app.MapPost("/auth/bootstrap", async (HttpContext http, TokenService tokens, BootstrapRequest body) =>
        {
            if (await tokens.AnyActiveAsync()) return Results.Json(new { error = "Tokens already exist." }, statusCode: 409);
            if (!System.Net.IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? System.Net.IPAddress.None))
                return Results.Json(new { error = "The first token can only be created from the machine the server runs on." }, statusCode: 403);
            var token = await tokens.CreateAsync(string.IsNullOrWhiteSpace(body.Name) ? "owner" : body.Name, "bootstrap");
            Cookie(http, token);
            return Results.Ok(new { token, note = "Store this token now; it is shown only once." });
        }).AllowAnonymous().DisableAntiforgery();

        app.MapGet("/auth/state", async (TokenService tokens) => new { needsBootstrap = !await tokens.AnyActiveAsync() }).AllowAnonymous();
    }

    private static void Cookie(HttpContext http, string token) =>
        http.Response.Cookies.Append(AuthExtensions.CookieName, token, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = http.Request.IsHttps, Expires = DateTimeOffset.UtcNow.AddDays(30), Path = "/",
        });
}

public sealed record LoginRequest(string Token);
public sealed record BootstrapRequest(string? Name);

public static class ErrorMapping
{
    public static async Task HandleAsync(HttpContext http)
    {
        var ex = http.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        var (status, title) = ex switch
        {
            PlanException or ScenarioException or DemandException or PeopleException or TrainingException or HostException or AdminException
                or SkillRatingException or ArgumentException or InvalidDataException or FormatException => (400, ex.Message),
            FileNotFoundException => (404, "File not found."),
            _ => (500, "Unexpected error. See the server log."),
        };
        if (ex is not null && status == 400 && ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)) status = 404;
        if (status == 500) http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("EOS").LogError(ex, "Unhandled exception");
        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(new { type = "about:blank", title, status }, options: null, contentType: "application/problem+json");
    }
}
