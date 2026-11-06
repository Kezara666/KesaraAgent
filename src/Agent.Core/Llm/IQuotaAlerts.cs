namespace Agent.Core.Llm;

/// <summary>
/// Told when a profile has used most of its daily budget (NFR-9). Adapters tell a person. Token counts only,
/// never prompts or replies.
/// </summary>
public interface IQuotaAlerts
{
    Task RaiseAsync(string profileId, long usedTokens, long budgetTokens, CancellationToken cancellationToken);
}
