using Agent.Core.Ports;

namespace Agent.Core.Tasks;

/// <summary>
/// Runs one queued task to a terminal state. Failures are recorded on the task, not thrown,
/// so one bad task never stops the worker.
/// </summary>
public sealed class TaskProcessor(ITaskStore store, ILlmClient llm, TimeProvider clock)
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
            var output = await llm.CompleteAsync(task.Prompt, cancellationToken).ConfigureAwait(false);
            var done = running with
            {
                Status = AgentTaskStatus.Done,
                Result = output,
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
