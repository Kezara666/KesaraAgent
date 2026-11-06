using Agent.Core.Audit;
using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Persistence;

/// <summary>Append-only audit adapter. Exposes no update or delete path.</summary>
public sealed class EfAuditLog(IDbContextFactory<AgentDbContext> factory) : IAuditLog
{
    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Audit.Add(new AuditRow
        {
            Id = entry.Id,
            Time = entry.Time,
            Actor = entry.Actor,
            Action = entry.Action,
            Target = entry.Target,
            Tier = entry.Tier,
            Result = entry.Result,
            TraceId = entry.TraceId,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Audit.AsNoTracking()
            .OrderByDescending(audit => audit.Time)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(row => new AuditEntry(
            row.Id, row.Time, row.Actor, row.Action, row.Target, row.Tier, row.Result, row.TraceId)).ToList();
    }
}
