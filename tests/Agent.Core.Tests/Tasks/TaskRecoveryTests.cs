using Agent.Core.Tasks;
using Agent.Core.Tests.Fakes;
using Xunit;

namespace Agent.Core.Tests.Tasks;

public sealed class TaskRecoveryTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FR81QueuedTaskSurvivesARestartAndIsRedispatched()
    {
        var store = new InMemoryTaskStore();
        var clock = new FakeClock(Start);
        var before = new TaskService(store, new InMemoryTaskDispatcher(), clock);
        var submitted = await before.SubmitAsync("keep me", idempotencyKey: null, CancellationToken.None);

        // Simulate a restart: a new dispatcher holds nothing, but the store still has the queued task.
        var afterRestart = new InMemoryTaskDispatcher();
        var recovered = await TaskRecovery.RecoverAsync(store, afterRestart, clock, CancellationToken.None);

        Assert.Equal(1, recovered);
        Assert.Equal([submitted.Task.Id], afterRestart.Enqueued);
    }

    [Fact]
    public async Task FR81TaskInterruptedMidRunIsReturnedToTheQueue()
    {
        var store = new InMemoryTaskStore();
        var clock = new FakeClock(Start);
        var id = Guid.CreateVersion7();
        await store.AddAsync(new AgentTask(id, "interrupted", AgentTaskStatus.Running, null, null, null, Start, Start), CancellationToken.None);
        var dispatcher = new InMemoryTaskDispatcher();

        await TaskRecovery.RecoverAsync(store, dispatcher, clock, CancellationToken.None);

        var task = await store.GetAsync(id, CancellationToken.None);
        Assert.Equal(AgentTaskStatus.Queued, task!.Status);
        Assert.Equal([id], dispatcher.Enqueued);
    }

    [Fact]
    public async Task FR81FinishedTasksAreNotRedispatched()
    {
        var store = new InMemoryTaskStore();
        await store.AddAsync(new AgentTask(Guid.CreateVersion7(), "done", AgentTaskStatus.Done, null, "ok", null, Start, Start), CancellationToken.None);
        var dispatcher = new InMemoryTaskDispatcher();

        var recovered = await TaskRecovery.RecoverAsync(store, dispatcher, new FakeClock(Start), CancellationToken.None);

        Assert.Equal(0, recovered);
        Assert.Empty(dispatcher.Enqueued);
    }
}
