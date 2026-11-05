using Agent.Core.Tasks;
using Agent.Core.Tests.Fakes;
using Xunit;

namespace Agent.Core.Tests.Tasks;

public sealed class TaskServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FR81SubmitQueuesTaskWithQueuedStatus()
    {
        var store = new InMemoryTaskStore();
        var queue = new InMemoryTaskDispatcher();
        var service = new TaskService(store, queue, new FakeClock(Start));

        var result = await service.SubmitAsync("summarize today's notes", idempotencyKey: null, CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(AgentTaskStatus.Queued, result.Task.Status);
        Assert.Equal(new[] { result.Task.Id }, queue.Enqueued);
    }

    [Fact]
    public async Task FR85ReusedIdempotencyKeyReturnsTheExistingTaskWithoutRequeuing()
    {
        var store = new InMemoryTaskStore();
        var queue = new InMemoryTaskDispatcher();
        var service = new TaskService(store, queue, new FakeClock(Start));

        var first = await service.SubmitAsync("first", "retry-key-1", CancellationToken.None);
        var second = await service.SubmitAsync("first", "retry-key-1", CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Task.Id, second.Task.Id);
        Assert.Single(queue.Enqueued);
    }

    [Fact]
    public async Task FR81BlankPromptIsRejected()
    {
        var service = new TaskService(new InMemoryTaskStore(), new InMemoryTaskDispatcher(), new FakeClock(Start));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync("   ", idempotencyKey: null, CancellationToken.None));
    }

    [Fact]
    public async Task FR81OverlongPromptIsRejected()
    {
        var service = new TaskService(new InMemoryTaskStore(), new InMemoryTaskDispatcher(), new FakeClock(Start));
        var prompt = new string('x', TaskService.MaxPromptLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync(prompt, idempotencyKey: null, CancellationToken.None));
    }
}
