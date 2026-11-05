using Agent.Core.Llm;

namespace Agent.Core.Tests.Fakes;

internal sealed class InMemoryUsageLedger : IUsageLedger
{
    private readonly Dictionary<(string, DateOnly), long> totals = [];

    public Task<long> GetUsedAsync(string profileId, DateOnly day, CancellationToken cancellationToken) =>
        Task.FromResult(totals.GetValueOrDefault((profileId, day)));

    public Task AddAsync(string profileId, DateOnly day, long tokens, CancellationToken cancellationToken)
    {
        totals[(profileId, day)] = totals.GetValueOrDefault((profileId, day)) + tokens;
        return Task.CompletedTask;
    }

    public long Total(string profileId, DateOnly day) => totals.GetValueOrDefault((profileId, day));
}
