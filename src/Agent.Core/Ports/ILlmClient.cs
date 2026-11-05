using Agent.Core.Llm;

namespace Agent.Core.Ports;

/// <summary>The agent's view of model access. <see cref="LlmRouter"/> is the production implementation.</summary>
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}
