using Agent.Core.Tasks;

namespace Agent.Core.Tests.Fakes;

internal sealed class InMemoryTaskStore : ITaskStore
{
    private readonly Dictionary<Guid, AgentTask> tasks = [];

    public Task AddAsync(AgentTask task, CancellationToken cancellationToken)
    {
        tasks[task.Id] = task;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AgentTask task, CancellationToken cancellationToken)
    {
        tasks[task.Id] = task;
        return Task.CompletedTask;
    }

    public Task<AgentTask?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(tasks.GetValueOrDefault(id));

    public Task<AgentTask?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(tasks.Values.FirstOrDefault(task => task.IdempotencyKey == idempotencyKey));

    public Task<IReadOnlyList<AgentTask>> ListAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentTask>>(tasks.Values
            .OrderByDescending(task => task.CreatedAt)
            .Take(limit)
            .ToList());

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<IReadOnlyList<AgentTask>> ListByStatusAsync(AgentTaskStatus status, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentTask>>(tasks.Values.Where(task => task.Status == status).Take(limit).ToList());
}
