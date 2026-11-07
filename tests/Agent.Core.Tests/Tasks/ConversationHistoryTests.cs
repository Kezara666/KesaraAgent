using Agent.Core.Agent;
using Agent.Core.Approvals;
using Agent.Core.Llm;
using Agent.Core.Ports;
using Agent.Core.Tasks;
using Agent.Core.Tests.Fakes;
using Agent.Core.ToolForge;
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

    [Fact]
    public async Task FR52ApprovedChangeContinuesTheConversation()
    {
        var store = new InMemoryTaskStore();
        var clock = new FakeClock(Start);
        var service = new TaskService(store, new InMemoryTaskDispatcher(), clock);
        var approvals = new InMemoryApprovalStore();
        var audit = new InMemoryAuditLog();
        var tools = new ToolRegistry([new NoteTool()]);
        var loop = new AgentLoop(new ProposingModel(), tools, tools, tools, approvals, audit, clock);
        var processor = new TaskProcessor(store, loop, clock);
        var conversation = Guid.CreateVersion7();

        var first = await service.SubmitAsync("save a note", null, CancellationToken.None, conversation);
        await processor.ProcessAsync(first.Task.Id, CancellationToken.None);
        var pending = Assert.Single(await approvals.ListAsync(ApprovalStatus.Pending, 10, CancellationToken.None));

        await new AgentToolActions(approvals, tools, audit, clock, service).DecideAsync(pending.Id, approve: true, "approver", CancellationToken.None);

        var inConversation = await store.ListConversationAsync(conversation, 100, CancellationToken.None);
        Assert.Equal(2, inConversation.Count);
        Assert.Contains(inConversation, task => task.Prompt.Contains("was approved and applied", StringComparison.Ordinal)
            && task.Prompt.Contains("write_note", StringComparison.Ordinal));
    }

    private static TaskProcessor Processor(ITaskStore store, ILlmClient llm, TimeProvider clock)
    {
        var tools = new ToolRegistry([]);
        var loop = new AgentLoop(llm, tools, tools, tools, new InMemoryApprovalStore(), new InMemoryAuditLog(), clock);
        return new TaskProcessor(store, loop, clock);
    }

    private sealed class ProposingModel : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ModelAnswer> ChatAsync(IReadOnlyList<ChatTurn> turns, IReadOnlyList<ModelTool> tools, CancellationToken cancellationToken)
        {
            if (turns.Any(turn => turn.Role == "tool"))
            {
                return Task.FromResult(new ModelAnswer("Saved after approval.", [], 1, 1, "fake"));
            }

            var call = new ModelToolCall("call-1", "write_note", new Dictionary<string, string> { ["text"] = "hi" });
            return Task.FromResult(new ModelAnswer(null, [call], 1, 1, "fake"));
        }
    }

    private sealed class NoteTool : IToolProvider
    {
        private static readonly IReadOnlyList<ToolDescriptor> Tools = [new("write_note", SideEffect.Write, ["text"], "Write a note.")];

        public ToolDescriptor? Find(string name) => Tools.FirstOrDefault(tool => tool.Name == name);

        public IReadOnlyList<ToolDescriptor> List() => Tools;

        public Task<string> InvokeAsync(string tool, IReadOnlyDictionary<string, string> inputs, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult("written");
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
