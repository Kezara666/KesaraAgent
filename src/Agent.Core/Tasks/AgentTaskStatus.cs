namespace Agent.Core.Tasks;

/// <summary>Lifecycle of an agent task. Transitions are owned by <see cref="TaskProcessor"/>.</summary>
public enum AgentTaskStatus
{
    Queued,
    Running,
    WaitingApproval,
    Done,
    Failed,
    Cancelled,
}
