using Agent.Core.Headless;
using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Persistence;

public sealed class EfScheduleStore(IDbContextFactory<AgentDbContext> factory) : IScheduleStore
{
    public async Task AddAsync(Schedule schedule, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Schedules.Add(ToRow(schedule));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Schedule schedule, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Schedules.Update(ToRow(schedule));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Schedule>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Schedules.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToRecord).ToList();
    }

    private static ScheduleRow ToRow(Schedule schedule) => new()
    {
        Id = schedule.Id,
        Name = schedule.Name,
        CronExpression = schedule.CronExpression,
        Prompt = schedule.Prompt,
        Enabled = schedule.Enabled,
        LastRunTicks = schedule.LastRunAt?.UtcTicks,
    };

    private static Schedule ToRecord(ScheduleRow row) => new(
        row.Id,
        row.Name,
        row.CronExpression,
        row.Prompt,
        row.Enabled,
        row.LastRunTicks is { } ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null);
}
