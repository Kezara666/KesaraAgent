using Agent.Core.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Persistence;

public sealed class EfApprovalStore(IDbContextFactory<AgentDbContext> factory) : IApprovalStore
{
    public async Task AddAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Approvals.Add(ToRow(request));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Approvals.Update(ToRow(request));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Approvals.AsNoTracking().FirstOrDefaultAsync(approval => approval.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<ApprovalRequest>> ListAsync(ApprovalStatus? status, int limit, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.Approvals.AsNoTracking().AsQueryable();
        if (status is { } filter)
        {
            var text = filter.ToString();
            query = query.Where(approval => approval.Status == text);
        }

        var rows = await query.OrderByDescending(approval => approval.CreatedAt).Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToRecord).ToList();
    }

    private static ApprovalRow ToRow(ApprovalRequest request) => new()
    {
        Id = request.Id,
        Kind = request.Kind,
        Target = request.Target,
        Payload = request.Payload,
        Status = request.Status.ToString(),
        CreatedAt = request.CreatedAt,
        ExpiresAt = request.ExpiresAt,
        DecidedAt = request.DecidedAt,
        ResultSummary = request.ResultSummary,
    };

    private static ApprovalRequest ToRecord(ApprovalRow row) => new(
        row.Id,
        row.Kind,
        row.Target,
        row.Payload,
        Enum.Parse<ApprovalStatus>(row.Status),
        row.CreatedAt,
        row.ExpiresAt,
        row.DecidedAt,
        row.ResultSummary);
}
