using Cronos;

namespace Agent.Core.Headless;

/// <summary>A recurring task. It runs with no client connected (FR-7.2, FR-9.8).</summary>
/// <param name="Id">Identifier.</param>
/// <param name="Name">Short label shown to the user.</param>
/// <param name="CronExpression">Five-field cron, in UTC.</param>
/// <param name="Prompt">The task submitted at each occurrence.</param>
/// <param name="Enabled">Disabled schedules are kept but do not run.</param>
/// <param name="LastRunAt">Last occurrence that was submitted.</param>
public sealed record Schedule(Guid Id, string Name, string CronExpression, string Prompt, bool Enabled, DateTimeOffset? LastRunAt);

public static class CronPreview
{
    /// <summary>Parses a cron expression, or returns the reason it is invalid.</summary>
    public static (CronExpression? Expression, string? Error) Parse(string cron)
    {
        try
        {
            return (CronExpression.Parse(cron, CronFormat.Standard), null);
        }
        catch (CronFormatException exception)
        {
            return (null, exception.Message);
        }
    }

    /// <summary>Shows the next occurrences so the user can confirm a schedule before it is saved (FR-9.8).</summary>
    public static IReadOnlyList<DateTimeOffset> NextOccurrences(string cron, DateTimeOffset from, int count)
    {
        var expression = Parse(cron).Expression ?? throw new ArgumentException("The cron expression is invalid.", nameof(cron));
        var results = new List<DateTimeOffset>();
        var cursor = from.UtcDateTime;

        for (var index = 0; index < count; index++)
        {
            var next = expression.GetNextOccurrence(cursor, TimeZoneInfo.Utc, inclusive: false);
            if (next is null)
            {
                break;
            }

            results.Add(new DateTimeOffset(DateTime.SpecifyKind(next.Value, DateTimeKind.Utc)));
            cursor = next.Value;
        }

        return results;
    }
}

public interface IScheduleStore
{
    Task AddAsync(Schedule schedule, CancellationToken cancellationToken);

    Task UpdateAsync(Schedule schedule, CancellationToken cancellationToken);

    Task<IReadOnlyList<Schedule>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Submits a scheduled task. Implemented by the task service so schedules use the normal pipeline.</summary>
public interface IScheduledJobRunner
{
    Task RunAsync(Schedule schedule, CancellationToken cancellationToken);
}

/// <summary>
/// Submits each due occurrence once. An occurrence is due when it falls after the last run and at or before now.
/// A missed window (for example, the PC was off) is submitted once, not once per missed slot.
/// </summary>
public sealed class SchedulerService(IScheduleStore store, IScheduledJobRunner jobs, TimeProvider clock)
{
    public async Task<IReadOnlyList<string>> TickAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var submitted = new List<string>();

        foreach (var schedule in await store.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!schedule.Enabled)
            {
                continue;
            }

            var expression = CronPreview.Parse(schedule.CronExpression).Expression;
            if (expression is null)
            {
                continue;
            }

            var since = (schedule.LastRunAt ?? now.AddMinutes(-1)).UtcDateTime;
            var next = expression.GetNextOccurrence(since, TimeZoneInfo.Utc, inclusive: false);
            if (next is null || new DateTimeOffset(DateTime.SpecifyKind(next.Value, DateTimeKind.Utc)) > now)
            {
                continue;
            }

            await jobs.RunAsync(schedule, cancellationToken).ConfigureAwait(false);
            await store.UpdateAsync(schedule with { LastRunAt = now }, cancellationToken).ConfigureAwait(false);
            submitted.Add(schedule.Name);
        }

        return submitted;
    }
}
