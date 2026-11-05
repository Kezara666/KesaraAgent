namespace Agent.Core.Approvals;

/// <summary>
/// A held action waiting for a human decision. The action runs only if it is approved before it expires.
/// </summary>
/// <param name="Id">Time-ordered identifier.</param>
/// <param name="Kind">What is held, for example <c>command</c>.</param>
/// <param name="Target">Short description shown to the approver, never secrets.</param>
/// <param name="Payload">The exact action to run if approved. Stored as given, executed as given.</param>
/// <param name="Status">Current state.</param>
/// <param name="CreatedAt">When it was requested.</param>
/// <param name="ExpiresAt">After this instant the request can no longer be approved (24 hours by default).</param>
/// <param name="DecidedAt">When it was approved or rejected.</param>
/// <param name="ResultSummary">Short outcome after execution, such as the exit code.</param>
public sealed record ApprovalRequest(
    Guid Id,
    string Kind,
    string Target,
    string Payload,
    ApprovalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt,
    string? ResultSummary);
