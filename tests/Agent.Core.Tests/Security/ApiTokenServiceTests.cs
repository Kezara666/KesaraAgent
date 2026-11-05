using Agent.Core.Security;
using Xunit;

namespace Agent.Core.Tests.Security;

public sealed class ApiTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FR84StoredTokenIsTheHashNotTheRawValue()
    {
        var store = new InMemoryTokenStore();

        var (record, raw) = await ApiTokenService.CreateAsync(store, "dashboard", ["read"], Now, CancellationToken.None);

        Assert.Equal(ApiTokenService.Hash(raw), record.TokenHash);
        Assert.NotEqual(raw, record.TokenHash);
        Assert.DoesNotContain(raw, store.Stored.Select(token => token.TokenHash));
    }

    [Fact]
    public async Task FR84TwoTokensNeverMatch()
    {
        var store = new InMemoryTokenStore();

        var (_, first) = await ApiTokenService.CreateAsync(store, "a", ["read"], Now, CancellationToken.None);
        var (_, second) = await ApiTokenService.CreateAsync(store, "b", ["read"], Now, CancellationToken.None);

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("superuser")]
    [InlineData("READ")]
    public async Task FR84UnknownScopesAreRefused(string scope)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ApiTokenService.CreateAsync(new InMemoryTokenStore(), "x", [scope], Now, CancellationToken.None));
    }

    [Fact]
    public async Task FR84TokenNeedsAtLeastOneScope()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ApiTokenService.CreateAsync(new InMemoryTokenStore(), "x", [], Now, CancellationToken.None));
    }

    private sealed class InMemoryTokenStore : IApiTokenStore
    {
        private readonly List<ApiTokenRecord> items = [];

        public IReadOnlyList<ApiTokenRecord> Stored => items;

        public Task AddAsync(ApiTokenRecord token, CancellationToken cancellationToken)
        {
            items.Add(token);
            return Task.CompletedTask;
        }

        public Task<ApiTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(items.FirstOrDefault(token => token.TokenHash == tokenHash));

        public Task<IReadOnlyList<ApiTokenRecord>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApiTokenRecord>>(items.ToList());

        public Task RevokeAsync(Guid id, DateTimeOffset revokedAt, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
