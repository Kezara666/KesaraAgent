namespace Agent.Core.Tasks;

/// <summary>Accepts, reads, and lists tasks. Submission is idempotent when a key is supplied (FR-8.5).</summary>
public sealed class TaskService(ITaskStore store, ITaskDispatcher queue, TimeProvider clock)
{
    public const int MaxPromptLength = 4000;

    /// <summary>
    /// Accepts a task. If the idempotency key was already used, returns the existing task
    /// and <c>Created</c> is false.
    /// </summary>
    public async Task<SubmitResult> SubmitAsync(string prompt, string? idempotencyKey, CancellationToken cancellationToken, Guid? conversationId = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(prompt));
        }

        if (prompt.Length > MaxPromptLength)
        {
            throw new ArgumentException($"Prompt must be {MaxPromptLength} characters or fewer.", nameof(prompt));
        }

        if (idempotencyKey is not null)
        {
            var existing = await store.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return new SubmitResult(existing, Created: false);
            }
        }

        var now = clock.GetUtcNow();
        var task = new AgentTask(
            Guid.CreateVersion7(),
            prompt,
            AgentTaskStatus.Queued,
            idempotencyKey,
            Result: null,
            Error: null,
            CreatedAt: now,
            UpdatedAt: now,
            ConversationId: conversationId);

        await store.AddAsync(task, cancellationToken).ConfigureAwait(false);
        await queue.EnqueueAsync(task.Id, cancellationToken).ConfigureAwait(false);

        return new SubmitResult(task, Created: true);
    }

    public Task<AgentTask?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<AgentTask>> ListAsync(int limit, CancellationToken cancellationToken) =>
        store.ListAsync(limit, cancellationToken);

    public Task<bool> IsStoreAvailableAsync(CancellationToken cancellationToken) =>
        store.IsAvailableAsync(cancellationToken);
}

/// <summary>Result of a submission. <c>Created</c> is false when an idempotent retry matched an existing task.</summary>
public sealed record SubmitResult(AgentTask Task, bool Created);
