using Agent.Core.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Persistence;

/// <summary>SQLite-backed task store. A factory is used because the store is a singleton.</summary>
public sealed class EfTaskStore(IDbContextFactory<AgentDbContext> factory) : ITaskStore
{
    public async Task AddAsync(AgentTask task, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Tasks.Add(ToRow(task));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(AgentTask task, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Tasks.Update(ToRow(task));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentTask?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(task => task.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToTask(row);
    }

    public async Task<AgentTask?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Tasks.AsNoTracking()
            .FirstOrDefaultAsync(task => task.IdempotencyKey == idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToTask(row);
    }

    public async Task<IReadOnlyList<AgentTask>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Tasks.AsNoTracking()
            .OrderByDescending(task => task.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToTask).ToList();
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TaskRow ToRow(AgentTask task) => new()
    {
        Id = task.Id,
        Prompt = task.Prompt,
        Status = task.Status.ToString(),
        IdempotencyKey = task.IdempotencyKey,
        Result = task.Result,
        Error = task.Error,
        CreatedAt = task.CreatedAt,
        UpdatedAt = task.UpdatedAt,
    };

    private static AgentTask ToTask(TaskRow row) => new(
        row.Id,
        row.Prompt,
        Enum.Parse<AgentTaskStatus>(row.Status),
        row.IdempotencyKey,
        row.Result,
        row.Error,
        row.CreatedAt,
        row.UpdatedAt);
}
