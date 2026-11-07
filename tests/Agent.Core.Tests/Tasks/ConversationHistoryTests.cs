using Agent.Core.Agent;
using Agent.Core.Llm;
using Agent.Core.Ports;
using Agent.Core.Tasks;
using Agent.Core.Tests.Fakes;
using Agent.Core.Workflows;
using Xunit;

namespace Agent.Core.Tests.Tasks;

public sealed class ConversationHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FR81TaskInTheSameConversationSeesTheEarlierExchange()
    {
        var store = new InMemoryTaskStore();
        var clock = new FakeClock(Start);
        var service = new TaskService(store, new InMemoryTaskDispatcher(), clock);
        var model = new CapturingModel();
        var processor = Processor(store, model, clock);
        var conversation = Guid.CreateVersion7();

        var first = await service.SubmitAsync("my name is Ana", null, CancellationToken.None, conversation);
        await processor.ProcessAsync(first.Task.Id, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        var second = await service.SubmitAsync("what is my name?", null, CancellationToken.None, conversation);
        await processor.ProcessAsync(second.Task.Id, CancellationToken.None);

        var seen = model.Seen[^1];
        Assert.Contains(seen, turn => turn.Role == "user" && turn.Content == "my name is Ana");
        Assert.Contains(seen, turn => turn.Role == "assistant" && turn.Content == "reply to: my name is Ana");
        Assert.Equal("what is my name?", seen[^1].Content);
    }

    [Fact]
    public async Task FR81TaskWithoutAConversationSeesNoEarlierTasks()
    {
        var store = new InMemoryTaskStore();
        var clock = new FakeClock(Start);
        var service = new TaskService(store, new InMemoryTaskDispatcher(), clock);
        var model = new CapturingModel();
        var processor = Processor(store, model, clock);

        var first = await service.SubmitAsync("my name is Ana", null, CancellationToken.None);
        await processor.ProcessAsync(first.Task.Id, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        var second = await service.SubmitAsync("what is my name?", null, CancellationToken.None);
        await processor.ProcessAsync(second.Task.Id, CancellationToken.None);

        Assert.DoesNotContain(model.Seen[^1], turn => turn.Role == "assistant");
    }

    private static TaskProcessor Processor(ITaskStore store, ILlmClient llm, TimeProvider clock)
    {
        var tools = new ToolRegistry([]);
        var loop = new AgentLoop(llm, tools, tools, tools, new InMemoryApprovalStore(), new InMemoryAuditLog(), clock);
        return new TaskProcessor(store, loop, clock);
    }

    private sealed class CapturingModel : ILlmClient
    {
        public List<IReadOnlyList<ChatTurn>> Seen { get; } = [];

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ModelAnswer> ChatAsync(IReadOnlyList<ChatTurn> turns, IReadOnlyList<ModelTool> tools, CancellationToken cancellationToken)
        {
            Seen.Add(turns.ToList());
            var last = turns.Last(turn => turn.Role == "user").Content;
            return Task.FromResult(new ModelAnswer("reply to: " + last, [], 1, 1, "fake"));
        }
    }
}
