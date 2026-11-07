using Agent.Core.Agent;

namespace Agent.Core.Tasks;

/// <summary>
/// Runs one queued task to a terminal state through the agent loop. Failures are recorded on the task, not thrown,
/// so one bad task never stops the worker.
/// </summary>
public sealed class TaskProcessor(ITaskStore store, AgentLoop agent, TimeProvider clock)
{
    public async Task ProcessAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await store.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null || task.Status != AgentTaskStatus.Queued)
        {
            return;
        }

        var running = task with { Status = AgentTaskStatus.Running, UpdatedAt = clock.GetUtcNow() };
        await store.UpdateAsync(running, cancellationToken).ConfigureAwait(false);

        try
        {
            var run = await agent.RunAsync(task.Prompt, "task", task.Id.ToString("N"), traceId: null, cancellationToken)
                .ConfigureAwait(false);
            var text = run.PendingApprovals.Count == 0
                ? run.Text
                : $"{run.Text}\n\nWaiting for approval: {string.Join(", ", run.PendingApprovals)}";

            var done = running with
            {
                Status = AgentTaskStatus.Done,
                Result = text,
                UpdatedAt = clock.GetUtcNow(),
            };
            await store.UpdateAsync(done, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Store only the exception type. Messages can carry prompt or secret fragments.
            var failed = running with
            {
                Status = AgentTaskStatus.Failed,
                Error = exception.GetType().Name,
                UpdatedAt = clock.GetUtcNow(),
            };
            await store.UpdateAsync(failed, cancellationToken).ConfigureAwait(false);
        }
    }
}
