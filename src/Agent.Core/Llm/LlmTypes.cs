namespace Agent.Core.Llm;

/// <summary>A model call from the agent. <paramref name="RequiresToolCalling"/> excludes models without tool support.</summary>
public sealed record LlmRequest(string Prompt, bool RequiresToolCalling = false);

/// <summary>The answer and the profile that produced it, so every call can be traced and budgeted.</summary>
public sealed record LlmResponse(string Text, string ProfileId, string Model, long TokensUsed);

/// <summary>
/// A configured model. Capabilities are declared here, not inferred from the model name,
/// so the planner only uses a model for what it is marked to do (FR-4.3).
/// </summary>
/// <param name="Id">Stable identifier used in traces and budgets, for example <c>nim-nemotron</c>.</param>
/// <param name="ProviderId">Which provider serves this model.</param>
/// <param name="Model">The provider's model name.</param>
/// <param name="SupportsToolCalling">Whether the model reliably calls tools.</param>
/// <param name="ContextTokens">Largest prompt, in estimated tokens, the model accepts.</param>
/// <param name="DailyTokenBudget">Tokens this profile may use per UTC day. Use <c>long.MaxValue</c> for unlimited.</param>
/// <param name="Enabled">Disabled profiles are skipped without being removed from config.</param>
public sealed record ModelProfile(
    string Id,
    string ProviderId,
    string Model,
    bool SupportsToolCalling,
    int ContextTokens,
    long DailyTokenBudget,
    bool Enabled);
