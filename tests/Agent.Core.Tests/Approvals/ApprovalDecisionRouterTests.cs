using Agent.Core.Approvals;
using Agent.Core.Governance;
using Agent.Core.Shell;
using Agent.Core.Tests.Fakes;
using Agent.Core.Tests.ToolForge;
using Agent.Core.ToolForge;
using Agent.Core.Workflows;
using Xunit;

namespace Agent.Core.Tests.Approvals;

public sealed class ApprovalDecisionRouterTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const string GoodSource = "namespace Generated.Tools; public static class NoteWriter { public static string Run(string input) => input; }";
    private const string GoodTests = "namespace Generated.Tools.Tests; public class NoteWriterTests { }";

    [Fact]
    public async Task FR54RouterDecidesAHeldCommandThroughTheCommandPath()
    {
        var shell = new CountingShell();
        var approvals = new InMemoryApprovalStore();
        var commands = new GovernedCommandService(shell, new InMemoryAuditLog(), approvals, new FakeClock(Start));
        var router = Router(commands: commands, approvals: approvals);
        var pending = (CommandPending)await commands.SubmitAsync("hostname", ShellKind.PowerShell, Timeout, "alice", null, CancellationToken.None);

        var decided = await router.DecideAsync(pending.ApprovalId, approve: true, "approver", null, CancellationToken.None);

        Assert.Equal(ApprovalStatus.Executed, decided.Status);
        Assert.Equal(1, shell.Runs);
    }

    [Fact]
    public async Task FR54RouterDecidesAHeldWorkflowPlanThroughTheWorkflowService()
    {
        var approvals = new InMemoryApprovalStore();
        var store = new RouterWorkflowStore();
        var catalog = new RouterCatalog(new ToolDescriptor("notify_push", SideEffect.ExternalSend, ["message"]));
        var invoker = new RouterInvoker();
        var runner = new WorkflowRunner(catalog, invoker, store, new InMemoryAuditLog(), new FakeClock(Start));
        var workflows = new WorkflowService(catalog, store, approvals, new InMemoryAuditLog(), runner, new FakeClock(Start));
        var router = Router(workflows: workflows, approvals: approvals);
        var plan = new WorkflowPlan([new WorkflowStep("s1", "notify_push", new Dictionary<string, string> { ["message"] = "hi" })]);
        var run = await workflows.SubmitAsync(plan, "trace", CancellationToken.None);
        var approval = (await approvals.ListAsync(ApprovalStatus.Pending, 10, CancellationToken.None)).Single();

        await router.DecideAsync(approval.Id, approve: true, "approver", null, CancellationToken.None);

        Assert.Equal(RunStatus.Completed, (await store.GetRunAsync(run.RunId, CancellationToken.None))!.Status);
        Assert.Equal(1, invoker.Calls);
    }

    [Fact]
    public async Task FR54RouterDecidesAPendingToolPromotionThroughTheForge()
    {
        var approvals = new InMemoryApprovalStore();
        var repository = new InMemoryToolRepository();
        var forge = new ToolForgeService(
            new QueueGenerator(new GeneratedTool(GoodSource, GoodTests)),
            new FakeCompiler(true),
            new FakeTestRunner(true),
            repository,
            new InMemoryToolHistory(),
            approvals,
            new InMemoryAuditLog(),
            new FakeClock(Start));
        var router = Router(toolForge: forge, approvals: approvals);
        var spec = new ToolSpec("write_note", "notes", "Append a note", SideEffect.Write, NeedsNetwork: false);
        var pending = (ForgeOutcome.PendingApproval)await forge.ForgeAsync(spec, "planner", CancellationToken.None);

        await router.DecideAsync(pending.ApprovalId, approve: true, "approver", null, CancellationToken.None);

        Assert.Contains("write_note", repository.Promoted);
    }

    [Fact]
    public async Task FR54UnknownApprovalKindIsRefusedAndNothingRuns()
    {
        var approvals = new InMemoryApprovalStore();
        var shell = new CountingShell();
        var router = Router(approvals: approvals, shell: shell);
        var id = Guid.CreateVersion7();
        await approvals.AddAsync(
            new ApprovalRequest(id, "mystery.kind", "x", "{}", ApprovalStatus.Pending, Start, Start.AddHours(24), null, null),
            CancellationToken.None);

        await Assert.ThrowsAsync<ApprovalStateException>(() =>
            router.DecideAsync(id, approve: true, "approver", null, CancellationToken.None));

        Assert.Equal(0, shell.Runs);
        Assert.Equal(ApprovalStatus.Pending, (await approvals.GetAsync(id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task FR54UnknownApprovalIsReportedAsNotFound()
    {
        var router = Router();

        await Assert.ThrowsAsync<ApprovalNotFoundException>(() =>
            router.DecideAsync(Guid.CreateVersion7(), approve: true, "approver", null, CancellationToken.None));
    }

    private static ApprovalDecisionRouter Router(
        InMemoryApprovalStore? approvals = null,
        GovernedCommandService? commands = null,
        WorkflowService? workflows = null,
        ToolForgeService? toolForge = null,
        CountingShell? shell = null)
    {
        approvals ??= new InMemoryApprovalStore();
        shell ??= new CountingShell();
        commands ??= new GovernedCommandService(shell, new InMemoryAuditLog(), approvals, new FakeClock(Start));
        workflows ??= BuildWorkflows(approvals);
        toolForge ??= BuildForge(approvals);
        return new ApprovalDecisionRouter(approvals, commands, workflows, toolForge);
    }

    private static WorkflowService BuildWorkflows(InMemoryApprovalStore approvals)
    {
        var store = new RouterWorkflowStore();
        var catalog = new RouterCatalog();
        var runner = new WorkflowRunner(catalog, new RouterInvoker(), store, new InMemoryAuditLog(), new FakeClock(Start));
        return new WorkflowService(catalog, store, approvals, new InMemoryAuditLog(), runner, new FakeClock(Start));
    }

    private static ToolForgeService BuildForge(InMemoryApprovalStore approvals) => new(
        new QueueGenerator(),
        new FakeCompiler(true),
        new FakeTestRunner(true),
        new InMemoryToolRepository(),
        new InMemoryToolHistory(),
        approvals,
        new InMemoryAuditLog(),
        new FakeClock(Start));

    private sealed class CountingShell : IShellRunner
    {
        public int Runs { get; private set; }

        public Task<ShellResult> RunAsync(ShellRequest request, CancellationToken cancellationToken)
        {
            Runs++;
            return Task.FromResult(new ShellResult(ExitCode: 0, Stdout: string.Empty, Stderr: string.Empty, TimedOut: false));
        }
    }

    private sealed class RouterCatalog(params ToolDescriptor[] tools) : IToolCatalog
    {
        public ToolDescriptor? Find(string name) => tools.FirstOrDefault(tool => tool.Name == name);
    }

    private sealed class RouterInvoker : IToolInvoker
    {
        public int Calls { get; private set; }

        public Task<string> InvokeAsync(string tool, IReadOnlyDictionary<string, string> inputs, string idempotencyKey, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult("ok");
        }
    }

    private sealed class RouterWorkflowStore : IWorkflowStore
    {
        private readonly Dictionary<string, WorkflowRun> runs = new(StringComparer.Ordinal);
        private readonly List<StepRecord> steps = [];

        public Task SaveRunAsync(WorkflowRun run, CancellationToken cancellationToken)
        {
            runs[run.RunId] = run;
            return Task.CompletedTask;
        }

        public Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult(runs.TryGetValue(runId, out var run) ? run : null);

        public Task SaveStepAsync(StepRecord record, CancellationToken cancellationToken)
        {
            steps.RemoveAll(step => step.RunId == record.RunId && step.StepId == record.StepId);
            steps.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StepRecord>> GetStepsAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StepRecord>>(steps.Where(step => step.RunId == runId).ToList());
    }
}
