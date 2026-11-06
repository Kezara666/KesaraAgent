using Agent.Core.Ports;

namespace Agent.Core.Tests.Fakes;

internal sealed class ScriptedLlmClient(Func<string, string> respond) : ILlmClient
{
    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(respond(prompt));
}
