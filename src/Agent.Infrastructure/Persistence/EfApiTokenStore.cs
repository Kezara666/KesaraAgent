using Agent.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Persistence;

public sealed class EfApiTokenStore(IDbContextFactory<AgentDbContext> factory) : IApiTokenStore
{
    public async Task AddAsync(ApiTokenRecord token, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ApiTokens.Add(ToRow(token));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.ApiTokens.AsNoTracking()
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<ApiTokenRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.ApiTokens.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToRecord).ToList();
    }

    public async Task RevokeAsync(Guid id, DateTimeOffset revokedAt, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.ApiTokens.FirstOrDefaultAsync(token => token.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The token does not exist.");
        row.RevokedAt = revokedAt;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ApiTokenRow ToRow(ApiTokenRecord token) => new()
    {
        Id = token.Id,
        Name = token.Name,
        TokenHash = token.TokenHash,
        Scopes = token.Scopes,
        CreatedAt = token.CreatedAt,
        RevokedAt = token.RevokedAt,
    };

    private static ApiTokenRecord ToRecord(ApiTokenRow row) => new(
        row.Id, row.Name, row.TokenHash, row.Scopes, row.CreatedAt, row.RevokedAt);
}
