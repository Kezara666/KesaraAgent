using Agent.Core.Agent;
using Agent.Core.Approvals;
using Agent.Core.Governance;
using Agent.Core.Llm;
using Agent.Core.Ports;
using Agent.Core.Tests.Fakes;
using Agent.Core.ToolForge;
using Agent.Core.Workflows;
using Xunit;

namespace Agent.Core.Tests.Agent;

public sealed class AgentLoopTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FR52ReadOnlyToolCallRunsAndItsResultGoesBackToTheModel()
    {
        var tools = new FakeTools();
        var model = new ScriptedModel(
            turns => turns.Any(turn => turn.Role == "tool") ? new ModelAnswer("The value is 42.", [], 1, 1, "p") : CallTool("lookup", ("key", "answer")));
        var loop = Loop(model, tools);

        var result = await loop.RunAsync("what is the answer?", "alice", "run-1", null, CancellationToken.None);

        Assert.Equal("The value is 42.", result.Text);
        Assert.Equal(1, tools.Runs);
        Assert.Contains(model.Seen.Last(), turn => turn.Role == "tool" && turn.Content == "value-42");
    }

    [Fact]
    public async Task FR52WriteToolCallIsHeldForApprovalAndNotRun()
    {
        var tools = new FakeTools();
        var approvals = new InMemoryApprovalStore();
        var model = new ScriptedModel(turns => turns.Any(turn => turn.Role == "tool")
            ? new ModelAnswer("I have proposed the change.", [], 1, 1, "p")
            : CallTool("write_note", ("text", "hello")));
        var loop = Loop(model, tools, approvals);

        var result = await loop.RunAsync("save a note", "alice", "run-2", null, CancellationToken.None);

        Assert.Equal(0, tools.Runs);
        Assert.Single(result.PendingApprovals);
        var held = await approvals.GetAsync(result.PendingApprovals[0], CancellationToken.None);
        Assert.Equal(AgentLoop.ApprovalKind, held!.Kind);
        Assert.Equal(ApprovalStatus.Pending, held.Status);
    }

    [Fact]
    public async Task FR52UnknownToolIsReportedToTheModelAndNothingRuns()
    {
        var tools = new FakeTools();
        var model = new ScriptedModel(turns => turns.Any(turn => turn.Role == "tool")
            ? new ModelAnswer("That tool does not exist.", [], 1, 1, "p")
            : CallTool("delete_everything", ("path", "/")));
        var loop = Loop(model, tools);

        var result = await loop.RunAsync("delete everything", "alice", "run-3", null, CancellationToken.None);

        Assert.Equal(0, tools.Runs);
        Assert.Contains(model.Seen.Last(), turn => turn.Role == "tool" && turn.Content!.Contains("does not exist", StringComparison.Ordinal));
        Assert.Equal("That tool does not exist.", result.Text);
    }

    [Fact]
    public async Task FR52MissingInputIsReportedToTheModel()
    {
        var tools = new FakeTools();
        var model = new ScriptedModel(turns => turns.Any(turn => turn.Role == "tool")
            ? new ModelAnswer("I need the key.", [], 1, 1, "p")
            : CallTool("lookup"));
        var loop = Loop(model, tools);

        await loop.RunAsync("look it up", "alice", "run-4", null, CancellationToken.None);

        Assert.Equal(0, tools.Runs);
        Assert.Contains(model.Seen.Last(), turn => turn.Role == "tool" && turn.Content!.Contains("missing inputs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FR52ModelThatNeverStopsIsCutOffAtTheStepLimit()
    {
        var tools = new FakeTools();
        var model = new ScriptedModel(_ => CallTool("lookup", ("key", "again")));
        var loop = Loop(model, tools);

        var result = await loop.RunAsync("loop forever", "alice", "run-5", null, CancellationToken.None);

        Assert.Equal(AgentLoop.MaxSteps, result.Steps);
        Assert.Equal(AgentLoop.MaxSteps, tools.Runs);
        Assert.Contains("step limit", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FR52ApprovedToolCallRunsExactlyOnceWithTheHeldArguments()
    {
        var tools = new FakeTools();
        var approvals = new InMemoryApprovalStore();
        var audit = new InMemoryAuditLog();
        var model = new ScriptedModel(turns => turns.Any(turn => turn.Role == "tool")
            ? new ModelAnswer("Proposed.", [], 1, 1, "p")
            : CallTool("write_note", ("text", "approved text")));
        var loop = Loop(model, tools, approvals, audit);
        var result = await loop.RunAsync("save a note", "alice", "run-6", null, CancellationToken.None);
        var actions = new AgentToolActions(approvals, tools, audit, new FakeClock(Start));

        var decided = await actions.DecideAsync(result.PendingApprovals[0], approve: true, "approver", CancellationToken.None);

        Assert.Equal(ApprovalStatus.Executed, decided.Status);
        Assert.Equal(1, tools.Runs);
        Assert.Equal("approved text", tools.LastInputs["text"]);
        await Assert.ThrowsAsync<ApprovalStateException>(() =>
            actions.DecideAsync(result.PendingApprovals[0], approve: true, "approver", CancellationToken.None));
        Assert.Equal(1, tools.Runs);
    }

    private static AgentLoop Loop(ILlmClient model, FakeTools tools, InMemoryApprovalStore? approvals = null, InMemoryAuditLog? audit = null)
    {
        var registry = new ToolRegistry([tools]);
        return new AgentLoop(
            model,
            registry,
            registry,
            registry,
            approvals ?? new InMemoryApprovalStore(),
            audit ?? new InMemoryAuditLog(),
            new FakeClock(Start));
    }

    private static ModelAnswer CallTool(string name, params (string Key, string Value)[] arguments) =>
        new(null, [new ModelToolCall("call-" + name, name, arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))], 1, 1, "p");

    private sealed class ScriptedModel(Func<IReadOnlyList<ChatTurn>, ModelAnswer> respond) : ILlmClient
    {
        public List<IReadOnlyList<ChatTurn>> Seen { get; } = [];

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ModelAnswer> ChatAsync(IReadOnlyList<ChatTurn> turns, IReadOnlyList<ModelTool> tools, CancellationToken cancellationToken)
        {
            Seen.Add(turns.ToList());
            return Task.FromResult(respond(turns));
        }
    }

    private sealed class FakeTools : IToolProvider
    {
        private static readonly IReadOnlyList<ToolDescriptor> Tools =
        [
            new("lookup", SideEffect.ReadOnly, ["key"], "Look up a value."),
            new("write_note", SideEffect.Write, ["text"], "Write a note."),
        ];

        public int Runs { get; private set; }

        public IReadOnlyDictionary<string, string> LastInputs { get; private set; } = new Dictionary<string, string>();

        public ToolDescriptor? Find(string name) => Tools.FirstOrDefault(tool => tool.Name == name);

        public IReadOnlyList<ToolDescriptor> List() => Tools;

        public Task<string> InvokeAsync(string tool, IReadOnlyDictionary<string, string> inputs, string idempotencyKey, CancellationToken cancellationToken)
        {
            Runs++;
            LastInputs = inputs;
            return Task.FromResult(tool == "lookup" ? "value-42" : "written");
        }
    }
}
