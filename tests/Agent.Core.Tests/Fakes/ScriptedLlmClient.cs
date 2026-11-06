using Agent.Core.Llm;
using Agent.Core.Ports;

namespace Agent.Core.Tests.Fakes;

internal sealed class ScriptedLlmClient(Func<string, string> respond) : ILlmClient
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new LlmResponse(respond(request.Prompt), "scripted", "scripted", TokensUsed: 0));
}
