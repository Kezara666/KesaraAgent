namespace Agent.Core.Tasks;

/// <summary>A unit of work submitted by a client and processed by the agent.</summary>
/// <param name="Id">Time-ordered identifier (UUIDv7).</param>
/// <param name="Prompt">The user's request, as submitted.</param>
/// <param name="Status">Current lifecycle state.</param>
/// <param name="IdempotencyKey">Client-supplied key that makes submission safe to retry.</param>
/// <param name="Result">Output when the task is done.</param>
/// <param name="Error">Short, non-sensitive failure reason when the task failed.</param>
/// <param name="CreatedAt">When the task was accepted.</param>
/// <param name="UpdatedAt">When the task last changed state.</param>
public sealed record AgentTask(
    Guid Id,
    string Prompt,
    AgentTaskStatus Status,
    string? IdempotencyKey,
    string? Result,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
