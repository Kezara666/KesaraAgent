using System.Security.Cryptography;
using System.Text;

namespace Agent.Core.Security;

/// <summary>A stored API token. The raw token is never stored, only its SHA-256 hash.</summary>
/// <param name="Id">Identifier, safe to show.</param>
/// <param name="Name">Who the token is for, for example <c>laptop-dashboard</c>.</param>
/// <param name="TokenHash">Lowercase hex SHA-256 of the raw token.</param>
/// <param name="Scopes">Comma-separated scopes: read, act, admin.</param>
/// <param name="CreatedAt">When the token was issued.</param>
/// <param name="RevokedAt">Set when the token is revoked. A revoked token is refused.</param>
public sealed record ApiTokenRecord(Guid Id, string Name, string TokenHash, string Scopes, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

/// <summary>Port for token storage.</summary>
public interface IApiTokenStore
{
    Task AddAsync(ApiTokenRecord token, CancellationToken cancellationToken);

    Task<ApiTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiTokenRecord>> ListAsync(CancellationToken cancellationToken);

    Task RevokeAsync(Guid id, DateTimeOffset revokedAt, CancellationToken cancellationToken);
}

/// <summary>Creates tokens and hashes them. Only the scopes listed here are valid.</summary>
public static class ApiTokenService
{
    public static readonly IReadOnlySet<string> ValidScopes = new HashSet<string>(StringComparer.Ordinal) { "read", "act", "admin" };

    /// <summary>Creates a token. The returned raw value is shown once and is not recoverable afterwards.</summary>
    public static async Task<(ApiTokenRecord Record, string RawToken)> CreateAsync(
        IApiTokenStore store,
        string name,
        IReadOnlyList<string> scopes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
        {
            throw new ArgumentException("Name must be 1 to 64 characters.", nameof(name));
        }

        if (scopes.Count == 0 || scopes.Any(scope => !ValidScopes.Contains(scope)))
        {
            throw new ArgumentException("Scopes must be one or more of read, act, admin.", nameof(scopes));
        }

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var record = new ApiTokenRecord(
            Guid.CreateVersion7(),
            name,
            Hash(raw),
            string.Join(',', scopes.Distinct(StringComparer.Ordinal).OrderBy(scope => scope, StringComparer.Ordinal)),
            now,
            RevokedAt: null);

        await store.AddAsync(record, cancellationToken).ConfigureAwait(false);
        return (record, raw);
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
