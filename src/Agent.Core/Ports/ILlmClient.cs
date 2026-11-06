namespace Agent.Core.Ports;

/// <summary>Port for model calls. Real providers arrive in feat/llm-gateway.</summary>
public interface ILlmClient
{
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken);
}
