using Agent.Core.Ports;

namespace Agent.Core.Llm;

/// <summary>
/// Routes each call through configured profiles in order. A profile is skipped when it is disabled,
/// lacks a needed capability, cannot fit the prompt, or has spent its daily budget. A provider failure
/// moves on to the next profile (FR-4.2). If nothing answers, the caller gets a clear error.
/// </summary>
public sealed class LlmRouter(
    IReadOnlyList<ModelProfile> profiles,
    IEnumerable<IModelProvider> providers,
    IUsageLedger ledger,
    TimeProvider clock) : ILlmClient
{
    private readonly Dictionary<string, IModelProvider> providersById =
        providers.ToDictionary(provider => provider.ProviderId, StringComparer.Ordinal);

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.Prompt);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var estimatedPromptTokens = EstimateTokens(request.Prompt);
        var attempts = new List<string>();

        foreach (var profile in profiles)
        {
            if (!IsEligible(profile, request, estimatedPromptTokens))
            {
                continue;
            }

            if (!providersById.TryGetValue(profile.ProviderId, out var provider))
            {
                attempts.Add($"{profile.Id}:no-provider");
                continue;
            }

            var used = await ledger.GetUsedAsync(profile.Id, today, cancellationToken).ConfigureAwait(false);
            if (used >= profile.DailyTokenBudget)
            {
                attempts.Add($"{profile.Id}:budget-exhausted");
                continue;
            }

            try
            {
                var reply = await provider.CompleteAsync(profile.Model, request.Prompt, cancellationToken)
                    .ConfigureAwait(false);

                var tokens = reply.InputTokens + reply.OutputTokens;
                await ledger.AddAsync(profile.Id, today, tokens, cancellationToken).ConfigureAwait(false);

                return new LlmResponse(reply.Text, profile.Id, profile.Model, tokens);
            }
            catch (ProviderException exception)
            {
                // Record the kind only. The message may contain provider details.
                attempts.Add($"{profile.Id}:{exception.Kind}");
            }
        }

        throw new LlmUnavailableException(attempts);
    }

    private static bool IsEligible(ModelProfile profile, LlmRequest request, int estimatedPromptTokens) =>
        profile.Enabled
        && (!request.RequiresToolCalling || profile.SupportsToolCalling)
        && estimatedPromptTokens <= profile.ContextTokens;

    /// <summary>Rough estimate used only for context fitting. Providers report real counts after the call.</summary>
    public static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);
}
