namespace Agent.Infrastructure.Persistence;

/// <summary>Database shape of a task. Kept separate from the Core record so the schema can change independently.</summary>
public sealed class TaskRow
{
    public Guid Id { get; set; }

    public string Prompt { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? IdempotencyKey { get; set; }

    public string? Result { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
