using Agent.Core.Llm;

namespace Agent.Core.Tests.Fakes;

/// <summary>A provider that either answers or fails with a chosen kind. Records how often it was called.</summary>
internal sealed class FakeProvider(string providerId, Func<string, ProviderReply>? answer = null, ProviderFailureKind? failWith = null)
    : IModelProvider
{
    public string ProviderId { get; } = providerId;

    public int Calls { get; private set; }

    public Task<ProviderReply> CompleteAsync(string model, string prompt, CancellationToken cancellationToken)
    {
        Calls++;

        if (failWith is { } kind)
        {
            throw new ProviderException(kind, "secret-detail-should-not-leak");
        }

        return Task.FromResult((answer ?? (_ => new ProviderReply("answer", 10, 5)))(model));
    }
}
