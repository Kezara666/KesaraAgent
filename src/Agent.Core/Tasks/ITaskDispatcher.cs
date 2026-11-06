namespace Agent.Core.Tasks;

/// <summary>Port for handing task IDs to the worker. Implemented by adapters in Agent.Infrastructure.</summary>
public interface ITaskDispatcher
{
    ValueTask EnqueueAsync(Guid taskId, CancellationToken cancellationToken);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
