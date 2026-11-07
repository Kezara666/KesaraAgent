using Agent.Core.Ports;

namespace Agent.Core.Llm;

/// <summary>
/// Routes each call through configured profiles in order. A profile is skipped when it is disabled,
/// lacks a needed capability, cannot fit the prompt, or has spent its daily budget. Rate limits and server errors
/// retry the same profile up to its limit, then the router moves on (FR-4.2). If nothing answers, the caller gets a
/// clear error. Plain completions and tool-enabled conversations share this one path.
/// </summary>
public sealed class LlmRouter(
    IReadOnlyList<ModelProfile> profiles,
    IEnumerable<IModelProvider> providers,
    IUsageLedger ledger,
    TimeProvider clock,
    IQuotaAlerts? alerts = null,
    TimeSpan? retryDelay = null) : ILlmClient
{
    public const int MaxAttemptsLimit = 5;

    private readonly Dictionary<string, IModelProvider> providersById =
        providers.ToDictionary(provider => provider.ProviderId, StringComparer.Ordinal);

    private readonly TimeSpan retryBackoff = retryDelay ?? TimeSpan.FromMilliseconds(500);

    // Profile and day pairs that already raised a quota alert, so a person is told once per profile per day.
    private readonly HashSet<string> alertedToday = new(StringComparer.Ordinal);

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.Prompt);

        var turns = new[] { new ChatTurn("user", request.Prompt) };
        var (reply, profile) = await RouteAsync(turns, [], request.RequiresToolCalling, cancellationToken).ConfigureAwait(false);
        return new LlmResponse(reply.Text ?? string.Empty, profile.Id, profile.Model, reply.InputTokens + reply.OutputTokens);
    }

    public async Task<ModelAnswer> ChatAsync(IReadOnlyList<ChatTurn> turns, IReadOnlyList<ModelTool> tools, CancellationToken cancellationToken)
    {
        var (reply, profile) = await RouteAsync(turns, tools, requiresToolCalling: tools.Count > 0, cancellationToken)
            .ConfigureAwait(false);
        return new ModelAnswer(reply.Text, reply.ToolCalls, reply.InputTokens, reply.OutputTokens, profile.Id);
    }

    private async Task<(ChatReply Reply, ModelProfile Profile)> RouteAsync(
        IReadOnlyList<ChatTurn> turns,
        IReadOnlyList<ModelTool> tools,
        bool requiresToolCalling,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var estimatedPromptTokens = EstimateTurns(turns, tools);
        var attempts = new List<string>();

        foreach (var profile in profiles)
        {
            if (!IsEligible(profile, requiresToolCalling, estimatedPromptTokens))
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

            var limit = Math.Clamp(profile.MaxAttempts, 1, MaxAttemptsLimit);
            for (var attempt = 1; attempt <= limit; attempt++)
            {
                try
                {
                    var reply = await provider.ChatAsync(profile.Model, turns, tools, cancellationToken).ConfigureAwait(false);

                    var tokens = reply.InputTokens + reply.OutputTokens;
                    await ledger.AddAsync(profile.Id, today, tokens, cancellationToken).ConfigureAwait(false);
                    await MaybeAlertAsync(profile, today, used + tokens, cancellationToken).ConfigureAwait(false);

                    return (reply, profile);
                }
                catch (ProviderException exception) when (IsTransient(exception.Kind) && attempt < limit)
                {
                    // Throttling and server errors may clear on their own, so the same profile is tried again.
                    await Task.Delay(retryBackoff * attempt, cancellationToken).ConfigureAwait(false);
                }
                catch (ProviderException exception)
                {
                    // Record the kind only. The message may contain provider details.
                    attempts.Add($"{profile.Id}:{exception.Kind}");
                    break;
                }
            }
        }

        throw new LlmUnavailableException(attempts);
    }

    private static bool IsTransient(ProviderFailureKind kind) =>
        kind is ProviderFailureKind.RateLimited or ProviderFailureKind.Unavailable;

    private async Task MaybeAlertAsync(ModelProfile profile, DateOnly today, long usedAfterCall, CancellationToken cancellationToken)
    {
        if (alerts is null || profile.DailyTokenBudget <= 0 || usedAfterCall * 100 < profile.DailyTokenBudget * 80)
        {
            return;
        }

        lock (alertedToday)
        {
            if (!alertedToday.Add($"{profile.Id}|{today:yyyy-MM-dd}"))
            {
                return;
            }
        }

        try
        {
            await alerts.RaiseAsync(profile.Id, usedAfterCall, profile.DailyTokenBudget, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An alert that cannot be sent must never fail the answer the person is waiting for.
        }
    }

    private static bool IsEligible(ModelProfile profile, bool requiresToolCalling, int estimatedPromptTokens) =>
        profile.Enabled
        && (!requiresToolCalling || profile.SupportsToolCalling)
        && estimatedPromptTokens <= profile.ContextTokens;

    /// <summary>Rough estimate used only for context fitting. Providers report real counts after the call.</summary>
    public static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);

    private static int EstimateTurns(IReadOnlyList<ChatTurn> turns, IReadOnlyList<ModelTool> tools)
    {
        long characters = 0;
        foreach (var turn in turns)
        {
            characters += turn.Content?.Length ?? 0;
            foreach (var call in turn.ToolCalls ?? [])
            {
                characters += call.Name.Length + call.Arguments.Sum(pair => pair.Key.Length + pair.Value.Length);
            }
        }

        foreach (var tool in tools)
        {
            characters += tool.Name.Length + tool.Description.Length + tool.RequiredInputs.Sum(input => input.Length);
        }

        return (int)Math.Min(int.MaxValue, Math.Max(1, characters / 4));
    }
}
