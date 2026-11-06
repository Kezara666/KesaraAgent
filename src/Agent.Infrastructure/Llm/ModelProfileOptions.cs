namespace Agent.Infrastructure.Llm;

/// <summary>Configuration shape for a model profile. Converted to the Core record at startup.</summary>
public sealed class ModelProfileOptions
{
    public string Id { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public bool SupportsToolCalling { get; set; }

    public int ContextTokens { get; set; } = 8000;

    public long DailyTokenBudget { get; set; } = 100_000;

    public bool Enabled { get; set; } = true;

    /// <summary>Tries for rate-limit and server failures on this profile before the next profile is used.</summary>
    public int MaxAttempts { get; set; } = 1;
}
