using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Agent.Core.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Agent.Api.Auth;

/// <summary>
/// Accepts <c>Authorization: Bearer &lt;token&gt;</c>. Two sources are checked. The bootstrap token hash in
/// configuration (<c>Agent:ApiTokenSha256</c>) carries every scope and exists so the first admin can be created.
/// Other tokens live in the token store with only the scopes they were issued with. Revoked tokens are refused.
/// Only hashes are stored anywhere; the raw token is never logged or saved.
/// </summary>
public sealed partial class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IConfiguration configuration,
    IApiTokenStore tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ApiToken";
    public const string ScopeClaimType = "scope";

    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var presented = header[BearerPrefix.Length..].Trim();
        var presentedHash = ApiTokenService.Hash(presented);

        var bootstrap = configuration["Agent:ApiTokenSha256"];
        if (!string.IsNullOrWhiteSpace(bootstrap)
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presentedHash),
                Encoding.UTF8.GetBytes(bootstrap.Trim().ToLowerInvariant())))
        {
            return Success("bootstrap-admin", [ApiScopes.Read, ApiScopes.Act, ApiScopes.Admin]);
        }

        var stored = await tokens.FindByHashAsync(presentedHash, Context.RequestAborted).ConfigureAwait(false);
        if (stored is null)
        {
            return AuthenticateResult.Fail("Invalid token.");
        }

        if (stored.RevokedAt is not null)
        {
            LogRevokedToken(Logger, stored.Name);
            return AuthenticateResult.Fail("Token revoked.");
        }

        return Success(stored.Name, stored.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries));
    }

    private static AuthenticateResult Success(string name, IEnumerable<string> scopes)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        claims.AddRange(scopes.Select(scope => new Claim(ScopeClaimType, scope)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A revoked API token was presented. Name: {Name}.")]
    private static partial void LogRevokedToken(ILogger logger, string name);
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
    public const string Admin = "admin";
}
