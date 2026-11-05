using Agent.Core.Shell;

namespace Agent.Core.Governance;

/// <summary>
/// What happened to a submitted command. Executed carries the shell result. Pending carries the approval to decide.
/// </summary>
public abstract record CommandSubmission;

public sealed record CommandExecuted(ShellResult Result) : CommandSubmission;

public sealed record CommandPending(Guid ApprovalId, DateTimeOffset ExpiresAt) : CommandSubmission;
