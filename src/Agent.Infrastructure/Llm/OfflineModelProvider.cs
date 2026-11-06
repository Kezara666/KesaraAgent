using Agent.Core.Llm;

namespace Agent.Infrastructure.Llm;

/// <summary>
/// Last-resort provider that needs no network and no key. Keeps the system completing tasks
/// when every hosted provider is down or out of budget (FR-4.4). It does not reason; it reports.
/// </summary>
public sealed class OfflineModelProvider : IModelProvider
{
    public const string Id = "offline";

    public string ProviderId => Id;

    public Task<ProviderReply> CompleteAsync(string model, string prompt, CancellationToken cancellationToken)
    {
        var text = $"[offline] No hosted model answered. Received a {prompt.Length}-character request.";
        return Task.FromResult(new ProviderReply(text, InputTokens: 0, OutputTokens: 0));
    }
}
