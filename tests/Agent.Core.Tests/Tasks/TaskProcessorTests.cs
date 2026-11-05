using Agent.Core.Tasks;
using Agent.Core.Tests.Fakes;
using Xunit;

namespace Agent.Core.Tests.Tasks;

public sealed class TaskProcessorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FR81ProcessorMovesQueuedTaskToDoneWithResult()
    {
        var store = new InMemoryTaskStore();
        var clock = new FakeClock(Start);
        var service = new TaskService(store, new InMemoryTaskDispatcher(), clock);
        var processor = new TaskProcessor(store, new ScriptedLlmClient(prompt => $"echo: {prompt}"), clock);

        var submitted = await service.SubmitAsync("hello", idempotencyKey: null, CancellationToken.None);
        await processor.ProcessAsync(submitted.Task.Id, CancellationToken.None);

        var finished = await service.GetAsync(submitted.Task.Id, CancellationToken.None);
        Assert.NotNull(finished);
        Assert.Equal(AgentTaskStatus.Done, finished.Status);
        Assert.Equal("echo: hello", finished.Result);
        Assert.Null(finished.Error);
    }

    [Fact]
    public async Task FR86ModelFailureMarksTaskFailedWithoutLeakingMessage()
    {
        var store = new InMemoryTaskStore();
        var clock = new FakeClock(Start);
        var service = new TaskService(store, new InMemoryTaskDispatcher(), clock);
        var processor = new TaskProcessor(
            store,
            new ScriptedLlmClient(_ => throw new InvalidOperationException("secret-token-abc123 in message")),
            clock);

        var submitted = await service.SubmitAsync("hello", idempotencyKey: null, CancellationToken.None);
        await processor.ProcessAsync(submitted.Task.Id, CancellationToken.None);

        var finished = await service.GetAsync(submitted.Task.Id, CancellationToken.None);
        Assert.NotNull(finished);
        Assert.Equal(AgentTaskStatus.Failed, finished.Status);
        Assert.Equal(nameof(InvalidOperationException), finished.Error);
        Assert.DoesNotContain("secret-token-abc123", finished.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FR81ProcessorIgnoresTasksThatAreNotQueued()
    {
        var store = new InMemoryTaskStore();
        var clock = new FakeClock(Start);
        var service = new TaskService(store, new InMemoryTaskDispatcher(), clock);
        var calls = 0;
        var processor = new TaskProcessor(store, new ScriptedLlmClient(_ => { calls++; return "ok"; }), clock);

        var submitted = await service.SubmitAsync("hello", idempotencyKey: null, CancellationToken.None);
        await processor.ProcessAsync(submitted.Task.Id, CancellationToken.None);
        await processor.ProcessAsync(submitted.Task.Id, CancellationToken.None);

        Assert.Equal(1, calls);
    }
}
