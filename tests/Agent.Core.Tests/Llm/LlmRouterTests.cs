using Agent.Core.Llm;
using Agent.Core.Tests.Fakes;
using Xunit;

namespace Agent.Core.Tests.Llm;

public sealed class LlmRouterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task FR42RateLimitedProfileFallsBackToTheNextProfile()
    {
        var first = new FakeProvider("p1", failWith: ProviderFailureKind.RateLimited);
        var second = new FakeProvider("p2", _ => new ProviderReply("from second", 3, 4));
        var router = Build(
            [Profile("a", "p1"), Profile("b", "p2")],
            [first, second]);

        var response = await router.CompleteAsync(new LlmRequest("hello"), CancellationToken.None);

        Assert.Equal("b", response.ProfileId);
        Assert.Equal("from second", response.Text);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task FR42UnavailableProviderAlsoFallsBack()
    {
        var router = Build(
            [Profile("a", "p1"), Profile("b", "p2")],
            [new FakeProvider("p1", failWith: ProviderFailureKind.Unavailable), new FakeProvider("p2")]);

        var response = await router.CompleteAsync(new LlmRequest("hello"), CancellationToken.None);

        Assert.Equal("b", response.ProfileId);
    }

    [Fact]
    public async Task FR42WhenEveryProfileFailsTheErrorCarriesKindsButNoProviderMessages()
    {
        var router = Build(
            [Profile("a", "p1"), Profile("b", "p2")],
            [new FakeProvider("p1", failWith: ProviderFailureKind.RateLimited),
             new FakeProvider("p2", failWith: ProviderFailureKind.Unavailable)]);

        var exception = await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            router.CompleteAsync(new LlmRequest("hello"), CancellationToken.None));

        Assert.Contains("a:RateLimited", exception.Attempts);
        Assert.Contains("b:Unavailable", exception.Attempts);
        Assert.DoesNotContain(exception.Attempts, attempt => attempt.Contains("secret-detail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FR43ProfileWithoutToolCallingIsSkippedWhenToolsAreNeeded()
    {
        var noTools = new FakeProvider("p1");
        var withTools = new FakeProvider("p2");
        var router = Build(
            [Profile("plain", "p1", supportsTools: false), Profile("tools", "p2", supportsTools: true)],
            [noTools, withTools]);

        var response = await router.CompleteAsync(new LlmRequest("call a tool", RequiresToolCalling: true), CancellationToken.None);

        Assert.Equal("tools", response.ProfileId);
        Assert.Equal(0, noTools.Calls);
    }

    [Fact]
    public async Task FR43ProfileWhoseContextIsTooSmallIsSkipped()
    {
        var small = new FakeProvider("p1");
        var large = new FakeProvider("p2");
        var router = Build(
            [Profile("small", "p1", contextTokens: 50), Profile("large", "p2", contextTokens: 10_000)],
            [small, large]);

        var longPrompt = new string('x', 400); // about 100 estimated tokens

        var response = await router.CompleteAsync(new LlmRequest(longPrompt), CancellationToken.None);

        Assert.Equal("large", response.ProfileId);
        Assert.Equal(0, small.Calls);
    }

    [Fact]
    public async Task NFR9ProfileThatSpentItsDailyBudgetIsSkipped()
    {
        var ledger = new InMemoryUsageLedger();
        await ledger.AddAsync("a", Today, 100, CancellationToken.None);
        var first = new FakeProvider("p1");
        var second = new FakeProvider("p2");
        var router = Build(
            [Profile("a", "p1", dailyBudget: 100), Profile("b", "p2", dailyBudget: 1000)],
            [first, second],
            ledger);

        var response = await router.CompleteAsync(new LlmRequest("hello"), CancellationToken.None);

        Assert.Equal("b", response.ProfileId);
        Assert.Equal(0, first.Calls);
    }

    [Fact]
    public async Task NFR9SuccessfulCallIsRecordedAgainstTheProfileForToday()
    {
        var ledger = new InMemoryUsageLedger();
        var router = Build([Profile("a", "p1", dailyBudget: 1000)], [new FakeProvider("p1")], ledger);

        await router.CompleteAsync(new LlmRequest("hello"), CancellationToken.None);

        Assert.Equal(15, ledger.Total("a", Today)); // 10 input + 5 output from the fake provider
    }

    [Fact]
    public async Task FR44OfflineLastResortAnswersWhenHostedProfilesFail()
    {
        var router = Build(
            [Profile("a", "p1"), Profile("offline", "offline")],
            [new FakeProvider("p1", failWith: ProviderFailureKind.RateLimited),
             new FakeProvider("offline", _ => new ProviderReply("offline answer", 0, 0))]);

        var response = await router.CompleteAsync(new LlmRequest("hello"), CancellationToken.None);

        Assert.Equal("offline", response.ProfileId);
        Assert.Equal("offline answer", response.Text);
    }

    [Fact]
    public async Task FR41DisabledProfileIsNeverCalled()
    {
        var disabled = new FakeProvider("p1");
        var router = Build(
            [Profile("a", "p1", enabled: false), Profile("b", "p2")],
            [disabled, new FakeProvider("p2")]);

        var response = await router.CompleteAsync(new LlmRequest("hello"), CancellationToken.None);

        Assert.Equal("b", response.ProfileId);
        Assert.Equal(0, disabled.Calls);
    }

    private static ModelProfile Profile(
        string id,
        string providerId,
        bool supportsTools = true,
        int contextTokens = 100_000,
        long dailyBudget = 1_000_000,
        bool enabled = true,
        int maxAttempts = 1) =>
        new(id, providerId, $"{id}-model", supportsTools, contextTokens, dailyBudget, enabled, maxAttempts);

    private static LlmRouter Build(
        IReadOnlyList<ModelProfile> profiles,
        IReadOnlyList<IModelProvider> providers,
        InMemoryUsageLedger? ledger = null) =>
        new(profiles, providers, ledger ?? new InMemoryUsageLedger(), new FakeClock(Now));

    [Fact]
    public async Task FR42TransientFailureIsRetriedOnTheSameProfile()
    {
        var provider = new FlakyProvider("p1", failures: 1, ProviderFailureKind.RateLimited);
        var router = new LlmRouter([Profile("a", "p1", maxAttempts: 3)], [provider], new InMemoryUsageLedger(), new FakeClock(Now), retryDelay: TimeSpan.Zero);

        var response = await router.CompleteAsync(new LlmRequest("hello"), CancellationToken.None);

        Assert.Equal("a", response.ProfileId);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task FR42RejectedRequestIsNotRetried()
    {
        var provider = new FakeProvider("p1", failWith: ProviderFailureKind.Rejected);
        var second = new FakeProvider("p2");
        var router = new LlmRouter(
            [Profile("a", "p1", maxAttempts: 3), Profile("b", "p2")],
            [provider, second],
            new InMemoryUsageLedger(),
            new FakeClock(Now),
            retryDelay: TimeSpan.Zero);

        var response = await router.CompleteAsync(new LlmRequest("hello"), CancellationToken.None);

        Assert.Equal("b", response.ProfileId);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task NFR9QuotaAlertFiresOnceWhenAProfileReachesEightyPercentOfItsBudget()
    {
        var ledger = new InMemoryUsageLedger();
        await ledger.AddAsync("a", Today, 70, CancellationToken.None);
        var alerts = new RecordingAlerts();
        var router = new LlmRouter(
            [Profile("a", "p1", dailyBudget: 100)],
            [new FakeProvider("p1")],
            ledger,
            new FakeClock(Now),
            alerts,
            retryDelay: TimeSpan.Zero);

        await router.CompleteAsync(new LlmRequest("hello"), CancellationToken.None);
        await router.CompleteAsync(new LlmRequest("hello"), CancellationToken.None);

        var alert = Assert.Single(alerts.Raised);
        Assert.Equal("a", alert.ProfileId);
        Assert.Equal(85, alert.Used);
        Assert.Equal(100, alert.Budget);
    }

    private sealed class FlakyProvider(string providerId, int failures, ProviderFailureKind kind) : IModelProvider
    {
        private int remaining = failures;

        public string ProviderId { get; } = providerId;

        public int Calls { get; private set; }

        public Task<ProviderReply> CompleteAsync(string model, string prompt, CancellationToken cancellationToken)
        {
            Calls++;
            if (remaining-- > 0)
            {
                throw new ProviderException(kind, "transient");
            }

            return Task.FromResult(new ProviderReply("ok", 10, 5));
        }
    }

    private sealed class RecordingAlerts : IQuotaAlerts
    {
        public List<(string ProfileId, long Used, long Budget)> Raised { get; } = [];

        public Task RaiseAsync(string profileId, long usedTokens, long budgetTokens, CancellationToken cancellationToken)
        {
            Raised.Add((profileId, usedTokens, budgetTokens));
            return Task.CompletedTask;
        }
    }
}
