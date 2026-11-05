using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Agent.Core.Llm;

namespace Agent.Infrastructure.Llm;

/// <summary>
/// Adapter for any OpenAI-compatible chat endpoint (NVIDIA NIM, OpenRouter, Groq, Gemini's compatible endpoint).
/// The base URL and the name of the environment variable holding the key come from configuration.
/// The key is read at call time and never stored, logged, or returned.
/// </summary>
public sealed class OpenAiCompatibleProvider(string providerId, HttpClient http, string apiKeyVariable) : IModelProvider
{
    public string ProviderId { get; } = providerId;

    public async Task<ProviderReply> CompleteAsync(string model, string prompt, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable(apiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ProviderException(ProviderFailureKind.Rejected, $"Environment variable {apiKeyVariable} is not set.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(new ChatRequest(model, [new ChatMessage("user", prompt)])),
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

            var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken).ConfigureAwait(false)
                ?? throw new ProviderException(ProviderFailureKind.Unavailable, "Empty response.");

            var text = body.Choices.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrEmpty(text))
            {
                throw new ProviderException(ProviderFailureKind.Unavailable, "Response had no text.");
            }

            return new ProviderReply(
                text,
                body.Usage?.PromptTokens ?? EstimateTokens(prompt),
                body.Usage?.CompletionTokens ?? EstimateTokens(text));
        }
    }

    private static long EstimateTokens(string text) => LlmRouter.EstimateTokens(text);

    public sealed record ChatRequest(string Model, IReadOnlyList<ChatMessage> Messages);

    public sealed record ChatMessage(string Role, string Content);

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
    }

    public sealed class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public long PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public long CompletionTokens { get; init; }
    }
}
