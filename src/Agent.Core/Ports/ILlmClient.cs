using Agent.Core.Llm;

namespace Agent.Core.Ports;

/// <summary>The agent's view of model access. <see cref="LlmRouter"/> is the production implementation.</summary>
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// One model call over a conversation, with the tools the model may use. The default answers from the last user
    /// message and offers no tool calls, so clients that do not support tools keep working.
    /// </summary>
    async Task<ModelAnswer> ChatAsync(IReadOnlyList<ChatTurn> turns, IReadOnlyList<ModelTool> tools, CancellationToken cancellationToken)
    {
        var last = turns.LastOrDefault(turn => turn.Role == "user")?.Content ?? string.Empty;
        var answer = await CompleteAsync(new LlmRequest(last), cancellationToken).ConfigureAwait(false);
        return new ModelAnswer(answer.Text, [], answer.TokensUsed, 0, answer.ProfileId);
    }
}

/// <summary>A conversation answer from the routed model, with the profile that produced it.</summary>
public sealed record ModelAnswer(string? Text, IReadOnlyList<ModelToolCall> ToolCalls, long InputTokens, long OutputTokens, string ProfileId);
