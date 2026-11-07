using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Core.Llm;

namespace Agent.Infrastructure.Llm;

/// <summary>
/// Adapter for any OpenAI-compatible chat endpoint (NVIDIA NIM, OpenRouter, Groq, Gemini's compatible endpoint).
/// The base URL and the name of the environment variable holding the key come from configuration.
/// The key is read at call time and never stored, logged, or returned. Tools are offered in the standard
/// <c>tools</c> format, and tool calls come back as <c>tool_calls</c>.
/// </summary>
public sealed class OpenAiCompatibleProvider(string providerId, HttpClient http, string apiKeyVariable) : IModelProvider
{
    public string ProviderId { get; } = providerId;

    public async Task<ProviderReply> CompleteAsync(string model, string prompt, CancellationToken cancellationToken)
    {
        var reply = await ChatAsync(model, [new ChatTurn("user", prompt)], [], cancellationToken).ConfigureAwait(false);
        return new ProviderReply(reply.Text ?? string.Empty, reply.InputTokens, reply.OutputTokens);
    }

    public async Task<ChatReply> ChatAsync(
        string model,
        IReadOnlyList<ChatTurn> turns,
        IReadOnlyList<ModelTool> tools,
        CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable(apiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ProviderException(ProviderFailureKind.Rejected, $"Environment variable {apiKeyVariable} is not set.");
        }

        var body = new WireRequest(
            model,
            turns.Select(ToWire).ToList(),
            tools.Count == 0 ? null : tools.Select(ToWireTool).ToList(),
            tools.Count == 0 ? null : "auto");

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ProviderException(ProviderFailureKind.Unavailable, exception.GetType().Name);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException(ProviderFailureKind.Unavailable, "Timed out.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new ProviderException(ProviderFailureKind.RateLimited, "Rate limited.");
            }

            if ((int)response.StatusCode >= 500)
            {
                throw new ProviderException(ProviderFailureKind.Unavailable, $"Server returned {(int)response.StatusCode}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(ProviderFailureKind.Rejected, $"Request rejected with {(int)response.StatusCode}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken).ConfigureAwait(false)
                ?? throw new ProviderException(ProviderFailureKind.Unavailable, "Empty response.");

            var message = payload.Choices.FirstOrDefault()?.Message;
            var calls = (message?.ToolCalls ?? []).Select(ToModelCall).ToList();
            var text = message?.Content;
            if (string.IsNullOrEmpty(text) && calls.Count == 0)
            {
                throw new ProviderException(ProviderFailureKind.Unavailable, "Response had no text.");
            }

            var lastUser = turns.LastOrDefault(turn => turn.Role == "user")?.Content ?? string.Empty;
            return new ChatReply(
                text,
                calls,
                payload.Usage?.PromptTokens ?? EstimateTokens(lastUser),
                payload.Usage?.CompletionTokens ?? EstimateTokens(text ?? string.Empty));
        }
    }

    private static WireMessage ToWire(ChatTurn turn) => new(
        turn.Role,
        turn.Content,
        turn.ToolCalls is null || turn.ToolCalls.Count == 0
            ? null
            : turn.ToolCalls.Select(call => new WireToolCall(
                call.Id,
                "function",
                new WireFunctionCall(call.Name, JsonSerializer.Serialize(call.Arguments)))).ToList(),
        turn.ToolCallId);

    private static WireTool ToWireTool(ModelTool tool)
    {
        var properties = tool.RequiredInputs.ToDictionary(
            input => input,
            _ => (object)new { type = "string" },
            StringComparer.Ordinal);
        return new WireTool(
            "function",
            new WireFunction(
                tool.Name,
                tool.Description,
                new WireParameters("object", properties, tool.RequiredInputs.ToList())));
    }

    private static ModelToolCall ToModelCall(ResponseToolCall call)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        var raw = call.Function?.Arguments;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            using var document = JsonDocument.Parse(raw);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                arguments[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText();
            }
        }

        return new ModelToolCall(call.Id ?? string.Empty, call.Function?.Name ?? string.Empty, arguments);
    }

    private static long EstimateTokens(string text) => LlmRouter.EstimateTokens(text);

    // Request shapes. OpenAI-compatible endpoints expect these lowercase field names. Without them the request is rejected.
    public sealed record WireRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<WireMessage> Messages,
        [property: JsonPropertyName("tools"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WireTool>? Tools,
        [property: JsonPropertyName("tool_choice"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToolChoice);

    public sealed record WireMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WireToolCall>? ToolCalls = null,
        [property: JsonPropertyName("tool_call_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToolCallId = null);

    public sealed record WireToolCall(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] WireFunctionCall Function);

    public sealed record WireFunctionCall(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] string Arguments);

    public sealed record WireTool(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] WireFunction Function);

    public sealed record WireFunction(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] WireParameters Parameters);

    public sealed record WireParameters(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, object> Properties,
        [property: JsonPropertyName("required")] IReadOnlyList<string> Required);

    // Response shapes.
    public sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice> Choices { get; init; } = [];

        [JsonPropertyName("usage")]
        public Usage? Usage { get; init; }
    }

    public sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChoiceMessage? Message { get; init; }
    }

    public sealed class ChoiceMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }

        [JsonPropertyName("tool_calls")]
        public List<ResponseToolCall>? ToolCalls { get; init; }
    }

    public sealed class ResponseToolCall
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("function")]
        public ResponseFunctionCall? Function { get; init; }
    }

    public sealed class ResponseFunctionCall
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("arguments")]
        public string? Arguments { get; init; }
    }

    public sealed class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public long PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public long CompletionTokens { get; init; }
    }
}
