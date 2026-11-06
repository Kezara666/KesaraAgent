using Agent.Core.Ports;

namespace Agent.Infrastructure.Llm;

/// <summary>
/// Offline placeholder so the API and worker run with no network and no keys.
/// Replaced by the provider gateway in feat/llm-gateway.
/// </summary>
public sealed class EchoLlmClient : ILlmClient
{
    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult($"[offline] received {prompt.Length} characters");
}
