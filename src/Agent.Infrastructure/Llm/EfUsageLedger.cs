using Agent.Core.Llm;
using Agent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Llm;

/// <summary>Persists daily token usage per profile, so budgets survive restarts (NFR-9).</summary>
public sealed class EfUsageLedger(IDbContextFactory<AgentDbContext> factory) : IUsageLedger
{
    public async Task<long> GetUsedAsync(string profileId, DateOnly day, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var key = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var row = await db.Usage.AsNoTracking()
            .FirstOrDefaultAsync(usage => usage.ProfileId == profileId && usage.Day == key, cancellationToken)
            .ConfigureAwait(false);
        return row?.Tokens ?? 0;
    }

    public async Task AddAsync(string profileId, DateOnly day, long tokens, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var key = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var row = await db.Usage.FirstOrDefaultAsync(usage => usage.ProfileId == profileId && usage.Day == key, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            db.Usage.Add(new UsageRow { ProfileId = profileId, Day = key, Tokens = tokens });
        }
        else
        {
            row.Tokens += tokens;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
