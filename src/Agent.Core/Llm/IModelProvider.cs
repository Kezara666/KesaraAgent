namespace Agent.Core.Llm;

/// <summary>
/// Port for one provider endpoint. Implementations map their own failures to <see cref="ProviderException"/>
/// so the router can decide whether to fall back. Vendor details stay in adapters.
/// </summary>
public interface IModelProvider
{
    string ProviderId { get; }

    Task<ProviderReply> CompleteAsync(string model, string prompt, CancellationToken cancellationToken);

    /// <summary>
    /// A conversation call that may offer tools. Providers without tool support answer from the last user message and
    /// return no tool calls.
    /// </summary>
    async Task<ChatReply> ChatAsync(string model, IReadOnlyList<ChatTurn> turns, IReadOnlyList<ModelTool> tools, CancellationToken cancellationToken)
    {
        var last = turns.LastOrDefault(turn => turn.Role == "user")?.Content ?? string.Empty;
        var reply = await CompleteAsync(model, last, cancellationToken).ConfigureAwait(false);
        return new ChatReply(reply.Text, [], reply.InputTokens, reply.OutputTokens);
    }
}

/// <summary>Raw reply from a provider, with the token counts it reported.</summary>
public sealed record ProviderReply(string Text, long InputTokens, long OutputTokens);

public enum ProviderFailureKind
{
    /// <summary>The provider is throttling the caller (HTTP 429 or equivalent).</summary>
    RateLimited,

    /// <summary>The provider failed, timed out, or returned a server error.</summary>
    Unavailable,

    /// <summary>The provider refused the request: bad key, unknown model, or missing configuration.</summary>
    Rejected,
}

/// <summary>A provider failure. The message is for logs only and is never returned to clients.</summary>
public sealed class ProviderException(ProviderFailureKind kind, string message) : Exception(message)
{
    public ProviderFailureKind Kind { get; } = kind;
}

/// <summary>Raised when no profile could answer. Carries the failure kinds, not the messages.</summary>
public sealed class LlmUnavailableException(IReadOnlyList<string> attempts)
    : InvalidOperationException("No model could answer the request.")
{
    public IReadOnlyList<string> Attempts { get; } = attempts;
}
