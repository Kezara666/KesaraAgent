namespace Agent.Infrastructure.Llm;

/// <summary>An OpenAI-compatible endpoint. The key is never stored here, only the name of the variable that holds it.</summary>
public sealed class ProviderOptions
{
    public string Id { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKeyVariable { get; set; } = string.Empty;
}
