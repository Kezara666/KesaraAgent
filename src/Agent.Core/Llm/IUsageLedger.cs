namespace Agent.Core.Llm;

/// <summary>Port for per-profile daily token usage. Adapters persist it so budgets survive restarts.</summary>
public interface IUsageLedger
{
    Task<long> GetUsedAsync(string profileId, DateOnly day, CancellationToken cancellationToken);

    Task AddAsync(string profileId, DateOnly day, long tokens, CancellationToken cancellationToken);
}
