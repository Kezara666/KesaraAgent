using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Agent.Api.Auth;

/// <summary>
/// Accepts <c>Authorization: Bearer &lt;token&gt;</c>. Only the SHA-256 hash of the token is stored in
/// configuration (<c>Agent:ApiTokenSha256</c>); the raw token never is.
/// </summary>
public sealed partial class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ApiToken";
    public const string ScopeClaimType = "scope";

    private const string BearerPrefix = "Bearer ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var expectedHash = configuration["Agent:ApiTokenSha256"];
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            LogMissingTokenHash(Logger);
            return Task.FromResult(AuthenticateResult.Fail("Authentication is not configured."));
        }

        var presented = header[BearerPrefix.Length..].Trim();
        var presentedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(presented))).ToLowerInvariant();

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presentedHash),
            Encoding.UTF8.GetBytes(expectedHash.Trim().ToLowerInvariant()));

        if (!matches)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid token."));
        }

        // One configured token currently carries every scope. Per-client scopes arrive with sec/auth.
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "api-client"),
            new Claim(ScopeClaimType, ApiScopes.Read),
            new Claim(ScopeClaimType, ApiScopes.Act),
            new Claim(ScopeClaimType, ApiScopes.Admin),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No API token hash is configured. All token requests are rejected.")]
    private static partial void LogMissingTokenHash(ILogger logger);
}

public static class ApiScopes
{
    public const string Read = "read";
    public const string Act = "act";
    public const string Admin = "admin";
}

public static class ApiPolicies
{
    public const string Read = "read";
    public const string Act = "act";
}
