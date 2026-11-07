using Agent.Core.Agent;
using Agent.Core.Llm;

namespace Agent.Core.Tasks;

/// <summary>
/// Runs one queued task to a terminal state through the agent loop. Failures are recorded on the task, not thrown,
/// so one bad task never stops the worker.
/// </summary>
public sealed class TaskProcessor(ITaskStore store, AgentLoop agent, TimeProvider clock)
{
    private const int MaxConversationTasks = 100;
    private const int MaxHistoryTurns = 10;
    private const int MaxAnswerCharacters = 2000;

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
            var history = await BuildHistoryAsync(task, cancellationToken).ConfigureAwait(false);
            var run = await agent.RunAsync(task.Prompt, "task", task.Id.ToString("N"), traceId: null, cancellationToken, history, task.ConversationId)
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

    /// <summary>
    /// Earlier answers in the same conversation, oldest first. Only completed tasks count, and each answer is shortened so
    /// the context stays small.
    /// </summary>
    private async Task<IReadOnlyList<ChatTurn>> BuildHistoryAsync(AgentTask task, CancellationToken cancellationToken)
    {
        if (task.ConversationId is not { } conversationId)
        {
            return [];
        }

        var earlier = await store.ListConversationAsync(conversationId, MaxConversationTasks, cancellationToken).ConfigureAwait(false);
        var completed = earlier
            .Where(item => item.Id != task.Id && item.Status == AgentTaskStatus.Done && item.Result is not null)
            .TakeLast(MaxHistoryTurns)
            .ToList();

        var turns = new List<ChatTurn>();
        foreach (var item in completed)
        {
            turns.Add(new ChatTurn("user", item.Prompt));
            turns.Add(new ChatTurn("assistant", Shorten(item.Result!)));
        }

        return turns;
    }

    private static string Shorten(string text) => text.Length <= MaxAnswerCharacters ? text : text[..MaxAnswerCharacters] + "\n[shortened]";
}
