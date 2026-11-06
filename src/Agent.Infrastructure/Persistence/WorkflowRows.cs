namespace Agent.Infrastructure.Persistence;

/// <summary>A workflow run. The plan is stored as JSON, exactly as it was reviewed.</summary>
public sealed class WorkflowRunRow
{
    public string RunId { get; set; } = string.Empty;

    public string PlanJson { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? TraceId { get; set; }

    public string? FailedStep { get; set; }
}

/// <summary>The stored outcome of one step in a run.</summary>
public sealed class WorkflowStepRow
{
    public string RunId { get; set; } = string.Empty;

    public string StepId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Output { get; set; } = string.Empty;

    public string? IdempotencyKey { get; set; }

    public int Attempts { get; set; }
}
