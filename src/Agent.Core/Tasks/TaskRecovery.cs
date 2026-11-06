namespace Agent.Core.Tasks;

/// <summary>
/// Restores work after a restart. Queued tasks are handed back to the dispatcher. Tasks that were running
/// when the process stopped are returned to the queue, because they never reached a terminal state.
/// This makes the database the durable queue, so no separate broker is needed for this workload.
/// </summary>
public static class TaskRecovery
{
    public const int BatchSize = 100;

    public static async Task<int> RecoverAsync(ITaskStore store, ITaskDispatcher dispatcher, TimeProvider clock, CancellationToken cancellationToken)
    {
        var interrupted = await store.ListByStatusAsync(AgentTaskStatus.Running, BatchSize, cancellationToken).ConfigureAwait(false);
        foreach (var task in interrupted)
        {
            await store.UpdateAsync(task with { Status = AgentTaskStatus.Queued, UpdatedAt = clock.GetUtcNow() }, cancellationToken)
                .ConfigureAwait(false);
        }

        var queued = await store.ListByStatusAsync(AgentTaskStatus.Queued, BatchSize, cancellationToken).ConfigureAwait(false);
        foreach (var task in queued)
        {
            await dispatcher.EnqueueAsync(task.Id, cancellationToken).ConfigureAwait(false);
        }

        return queued.Count;
    }
}
