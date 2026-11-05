namespace Agent.Infrastructure.Persistence;

public sealed class ApprovalRow
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? ResultSummary { get; set; }
}
