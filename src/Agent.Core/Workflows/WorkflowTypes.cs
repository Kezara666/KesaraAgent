using Agent.Core.ToolForge;

namespace Agent.Core.Workflows;

/// <summary>
/// One step of a plan. An input value starting with <c>$ref:</c> is a reference to an earlier step's output,
/// for example <c>$ref:s1</c>. Any other value is a literal.
/// </summary>
public sealed record WorkflowStep(string Id, string Tool, IReadOnlyDictionary<string, string> Inputs);

/// <summary>A fixed, reviewable plan. Once approved, it cannot grow (FR-9.9).</summary>
public sealed record WorkflowPlan(IReadOnlyList<WorkflowStep> Steps);

/// <summary>A tool the planner or the agent may use. <paramref name="Description"/> is shown to the model.</summary>
public sealed record ToolDescriptor(string Name, SideEffect SideEffect, IReadOnlyList<string> RequiredInputs, string Description = "");

public enum RunStatus
{
    Planned,
    WaitingApproval,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public enum StepStatus
{
    Completed,
    Failed,
}

/// <summary>Stored outcome of one step. Completed steps are never run again on resume (FR-9.6).</summary>
public sealed record StepRecord(
    string RunId,
    string StepId,
    StepStatus Status,
    string Output,
    string? IdempotencyKey,
    int Attempts);

/// <summary>One execution of a plan.</summary>
public sealed record WorkflowRun(
    string RunId,
    WorkflowPlan Plan,
    RunStatus Status,
    string? TraceId,
    string? FailedStep);
