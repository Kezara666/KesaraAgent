using Agent.Core.Headless;
using Agent.Core.Tasks;

namespace Agent.Infrastructure.Notifications;

/// <summary>
/// Submits each scheduled occurrence through the normal task pipeline. The idempotency key is the schedule and
/// its occurrence time, so a retry of the same tick never creates a second task.
/// </summary>
public sealed class TaskJobRunner(TaskService tasks, TimeProvider clock) : IScheduledJobRunner
{
    public async Task RunAsync(Schedule schedule, CancellationToken cancellationToken)
    {
        var occurrence = clock.GetUtcNow().ToUnixTimeSeconds();
        var key = $"schedule:{schedule.Id:N}:{occurrence}";
        await tasks.SubmitAsync(schedule.Prompt, key, cancellationToken).ConfigureAwait(false);
    }
}
