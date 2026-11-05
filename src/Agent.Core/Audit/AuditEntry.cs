namespace Agent.Core.Audit;

/// <summary>One immutable record of an action. Written once, never changed (NFR-13, FR-8.7).</summary>
/// <param name="Id">Time-ordered identifier.</param>
/// <param name="Time">When the action happened, UTC.</param>
/// <param name="Actor">Who acted: an API client, the agent, or the scheduler. Never a secret.</param>
/// <param name="Action">What happened, for example <c>http.POST</c> or <c>command.denied</c>.</param>
/// <param name="Target">What it touched: a path, a task ID, or a command name. Never a full command body with secrets.</param>
/// <param name="Tier">Autonomy tier of the action, when one applies.</param>
/// <param name="Result">Outcome, for example <c>200</c>, <c>Denied</c>, or <c>Executed</c>.</param>
/// <param name="TraceId">Links the record to the trace of the request or run.</param>
public sealed record AuditEntry(
    Guid Id,
    DateTimeOffset Time,
    string Actor,
    string Action,
    string Target,
    string? Tier,
    string Result,
    string? TraceId);
