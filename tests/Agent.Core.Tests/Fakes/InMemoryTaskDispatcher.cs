using Agent.Core.Tasks;

namespace Agent.Core.Tests.Fakes;

internal sealed class InMemoryTaskDispatcher : ITaskDispatcher
{
    public List<Guid> Enqueued { get; } = [];

    public ValueTask EnqueueAsync(Guid taskId, CancellationToken cancellationToken)
    {
        Enqueued.Add(taskId);
        return ValueTask.CompletedTask;
    }

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("The fake queue is inspected, not consumed.");
}
