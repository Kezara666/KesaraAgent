namespace Agent.Core.Tasks;

/// <summary>Port for task persistence. Implemented by adapters in Agent.Infrastructure.</summary>
public interface ITaskStore
{
    Task AddAsync(AgentTask task, CancellationToken cancellationToken);

    Task UpdateAsync(AgentTask task, CancellationToken cancellationToken);

    Task<AgentTask?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<AgentTask?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentTask>> ListAsync(int limit, CancellationToken cancellationToken);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentTask>> ListByStatusAsync(AgentTaskStatus status, int limit, CancellationToken cancellationToken);
}
