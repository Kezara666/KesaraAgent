using Agent.Core.Governance;
using Agent.Core.Tests.Fakes;
using Agent.Core.ToolForge;
using Xunit;

namespace Agent.Core.Tests.ToolForge;

public sealed class ToolForgeServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private const string GoodSource = "namespace Generated.Tools; public static class ParseCsv { public static string Run(string input) => input; }";
    private const string GoodTests = "namespace Generated.Tools.Tests; public class ParseCsvTests { }";

    private static ToolSpec ReadOnlySpec => new("parse_csv", "text", "Split a CSV line", SideEffect.ReadOnly, NeedsNetwork: false);

    private static ToolSpec WriteSpec => new("write_note", "notes", "Append a note", SideEffect.Write, NeedsNetwork: false);

    [Fact]
    public async Task FR51ReadOnlyToolPassingAllChecksIsPromotedWithACommit()
    {
        var repository = new InMemoryToolRepository();
        var history = new InMemoryToolHistory();
        var service = Build(new QueueGenerator(new GeneratedTool(GoodSource, GoodTests)), new FakeCompiler(true), new FakeTestRunner(true), repository, history);

        var outcome = await service.ForgeAsync(ReadOnlySpec, "planner", CancellationToken.None);

        var promoted = Assert.IsType<ForgeOutcome.Promoted>(outcome);
        Assert.Equal("tools/generated/text/parse_csv", promoted.Path);
        Assert.Equal("abc1234", promoted.Commit);
        Assert.Equal(["parse_csv"], repository.Promoted);
        Assert.Contains("FR-5.4 promote parse_csv", history.Commits.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FR51WriteToolWaitsForApprovalAndPromotesOnlyWhenApproved()
    {
        var approvals = new InMemoryApprovalStore();
        var repository = new InMemoryToolRepository();
        var service = Build(new QueueGenerator(new GeneratedTool(GoodSource, GoodTests)), new FakeCompiler(true), new FakeTestRunner(true), repository, new InMemoryToolHistory(), approvals);

        var outcome = await service.ForgeAsync(WriteSpec, "planner", CancellationToken.None);

        var pending = Assert.IsType<ForgeOutcome.PendingApproval>(outcome);
        Assert.Empty(repository.Promoted);
        Assert.Equal(["write_note"], repository.Staged);

        var decided = await service.DecideAsync(pending.ApprovalId, approve: true, "approver", CancellationToken.None);

        Assert.IsType<ForgeOutcome.Promoted>(decided);
        Assert.Equal(["write_note"], repository.Promoted);
    }

    [Fact]
    public async Task FR51RejectedPromotionNeverRuns()
    {
        var repository = new InMemoryToolRepository();
        var service = Build(new QueueGenerator(new GeneratedTool(GoodSource, GoodTests)), new FakeCompiler(true), new FakeTestRunner(true), repository, new InMemoryToolHistory());
        var pending = (ForgeOutcome.PendingApproval)await service.ForgeAsync(WriteSpec, "planner", CancellationToken.None);

        var decided = await service.DecideAsync(pending.ApprovalId, approve: false, "approver", CancellationToken.None);

        Assert.IsType<ForgeOutcome.Rejected>(decided);
        Assert.Empty(repository.Promoted);
    }

    [Fact]
    public async Task FR51GivesUpAfterThreeAttemptsAndReportsEachReason()
    {
        var generator = new QueueGenerator(new GeneratedTool("class Broken {", GoodTests));
        var compiler = new FakeCompiler(succeeds: false);
        var service = Build(generator, compiler, new FakeTestRunner(true), new InMemoryToolRepository(), new InMemoryToolHistory());

        var outcome = await service.ForgeAsync(ReadOnlySpec, "planner", CancellationToken.None);

        var rejected = Assert.IsType<ForgeOutcome.Rejected>(outcome);
        Assert.Equal(ToolForgeService.MaxAttempts, generator.Calls);
        Assert.Equal(ToolForgeService.MaxAttempts, rejected.Reasons.Count);
    }

    [Fact]
    public async Task FR52ForbiddenCodeNeverReachesTheCompilerAndFeedbackIsReturned()
    {
        const string forbidden = "namespace Generated.Tools; public static class Bad { public static string Run(string i) { System.Diagnostics.Process.Start(\"x\"); return i; } }";
        var generator = new QueueGenerator(new GeneratedTool(forbidden, GoodTests), new GeneratedTool(GoodSource, GoodTests));
        var compiler = new FakeCompiler(true);
        var service = Build(generator, compiler, new FakeTestRunner(true), new InMemoryToolRepository(), new InMemoryToolHistory());

        var outcome = await service.ForgeAsync(ReadOnlySpec, "planner", CancellationToken.None);

        Assert.IsType<ForgeOutcome.Promoted>(outcome);
        Assert.Equal(1, compiler.Calls);
        Assert.Contains("starts processes", generator.LastFeedback, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FR52CompileErrorStopsThePipelineBeforeTests()
    {
        var tests = new FakeTestRunner(true);
        var service = Build(new QueueGenerator(new GeneratedTool(GoodSource, GoodTests)), new FakeCompiler(false), tests, new InMemoryToolRepository(), new InMemoryToolHistory());

        await service.ForgeAsync(ReadOnlySpec, "planner", CancellationToken.None);

        Assert.Equal(0, tests.Calls);
    }

    [Fact]
    public async Task FR52FailingTestsKeepTheToolOutOfPromotion()
    {
        var repository = new InMemoryToolRepository();
        var service = Build(new QueueGenerator(new GeneratedTool(GoodSource, GoodTests)), new FakeCompiler(true), new FakeTestRunner(false), repository, new InMemoryToolHistory());

        var outcome = await service.ForgeAsync(ReadOnlySpec, "planner", CancellationToken.None);

        Assert.IsType<ForgeOutcome.Rejected>(outcome);
        Assert.Empty(repository.Promoted);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("Upper")]
    [InlineData("a")]
    [InlineData("has space")]
    public void FR52UnsafeToolNamesAreRejected(string name)
    {
        var spec = ReadOnlySpec with { Name = name };

        Assert.Throws<ArgumentException>(spec.Validate);
    }

    [Fact]
    public async Task FR51ApprovalForAnExpiredPromotionIsRefused()
    {
        var clock = new FakeClock(Start);
        var service = Build(new QueueGenerator(new GeneratedTool(GoodSource, GoodTests)), new FakeCompiler(true), new FakeTestRunner(true), new InMemoryToolRepository(), new InMemoryToolHistory(), clock: clock);
        var pending = (ForgeOutcome.PendingApproval)await service.ForgeAsync(WriteSpec, "planner", CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(25));

        await Assert.ThrowsAsync<ApprovalStateException>(() =>
            service.DecideAsync(pending.ApprovalId, approve: true, "approver", CancellationToken.None));
    }

    private static ToolForgeService Build(
        IToolGenerator generator,
        IToolCompiler compiler,
        IToolTestRunner tests,
        IToolRepository repository,
        IToolHistory history,
        InMemoryApprovalStore? approvals = null,
        FakeClock? clock = null) =>
        new(generator, compiler, tests, repository, history, approvals ?? new InMemoryApprovalStore(), new InMemoryAuditLog(), clock ?? new FakeClock(Start));
}
