using Agent.Core.Shell;

namespace Agent.Core.Governance;

/// <summary>Raised when policy refuses a command outright. Carries the decision for the caller.</summary>
public sealed class CommandNotAllowedException(CommandDecision decision)
    : InvalidOperationException(decision.Reason)
{
    public CommandDecision Decision { get; } = decision;
}
