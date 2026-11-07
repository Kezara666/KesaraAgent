namespace Agent.Core.Llm;

/// <summary>
/// A tool the model may ask for. The description and the required inputs are shown to the model. The agent checks
/// every call against the registry before anything runs.
/// </summary>
public sealed record ModelTool(string Name, string Description, IReadOnlyList<string> RequiredInputs);

/// <summary>A tool call the model asked for. Arguments are strings, because the agent maps them to tool inputs.</summary>
public sealed record ModelToolCall(string Id, string Name, IReadOnlyDictionary<string, string> Arguments);

/// <summary>
/// One message in a conversation. <c>Role</c> is system, user, assistant, or tool. Assistant turns may carry tool calls.
/// Tool turns carry the id of the call they answer.
/// </summary>
public sealed record ChatTurn(
    string Role,
    string? Content,
    IReadOnlyList<ModelToolCall>? ToolCalls = null,
    string? ToolCallId = null);

/// <summary>The model's answer: text, tool calls, or both, with the token counts it reported.</summary>
public sealed record ChatReply(string? Text, IReadOnlyList<ModelToolCall> ToolCalls, long InputTokens, long OutputTokens);
