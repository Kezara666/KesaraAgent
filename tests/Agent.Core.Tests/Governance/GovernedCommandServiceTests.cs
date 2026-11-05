using Agent.Core.Approvals;
using Agent.Core.Governance;
using Agent.Core.Shell;
using Agent.Core.Tests.Fakes;
using Xunit;

namespace Agent.Core.Tests.Governance;

public sealed class GovernedCommandServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task FR34DeniedCommandIsAuditedAndNeverRuns()
    {
        var shell = new RecordingShell();
        var audit = new InMemoryAuditLog();
        var service = Build(shell, audit, out _, new FakeClock(Start));

        await Assert.ThrowsAsync<CommandNotAllowedException>(() =>
            service.SubmitAsync("Remove-Item -Recurse C:\\", ShellKind.PowerShell, Timeout, "alice", null, CancellationToken.None));

        Assert.Equal(0, shell.Runs);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("command.denied", entry.Action);
        Assert.Equal("Denied", entry.Result);
        Assert.Equal("alice", entry.Actor);
    }

    [Fact]
    public async Task FR34AllowlistedCommandRunsAndIsAudited()
    {
        var shell = new RecordingShell();
        var audit = new InMemoryAuditLog();
        var service = Build(shell, audit, out _, new FakeClock(Start));

        var submission = await service.SubmitAsync("dotnet build", ShellKind.PowerShell, Timeout, "alice", null, CancellationToken.None);

        var executed = Assert.IsType<CommandExecuted>(submission);
        Assert.Equal(1, shell.Runs);
        Assert.Equal(0, executed.Result.ExitCode);
        Assert.Equal("command.executed", Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task FR34HeldCommandCreatesPendingApprovalAndDoesNotRun()
    {
        var shell = new RecordingShell();
        var approvals = new InMemoryApprovalStore();
        var service = new GovernedCommandService(shell, new InMemoryAuditLog(), approvals, new FakeClock(Start));

        var submission = await service.SubmitAsync("hostname", ShellKind.PowerShell, Timeout, "alice", null, CancellationToken.None);

        var pending = Assert.IsType<CommandPending>(submission);
        Assert.Equal(0, shell.Runs);
        var stored = await approvals.GetAsync(pending.ApprovalId, CancellationToken.None);
        Assert.Equal(ApprovalStatus.Pending, stored!.Status);
        Assert.Equal(Start.Add(GovernedCommandService.ApprovalLifetime), pending.ExpiresAt);
    }

    [Fact]
    public async Task FR34ApprovedHeldCommandRunsExactlyOnce()
    {
        var shell = new RecordingShell();
        var audit = new InMemoryAuditLog();
        var service = Build(shell, audit, out _, new FakeClock(Start));
        var pending = (CommandPending)await service.SubmitAsync("hostname", ShellKind.PowerShell, Timeout, "alice", null, CancellationToken.None);

        var decided = await service.DecideAsync(pending.ApprovalId, approve: true, "approver", null, CancellationToken.None);

        Assert.Equal(ApprovalStatus.Executed, decided.Status);
        Assert.Equal("Exit 0", decided.ResultSummary);
        Assert.Equal(1, shell.Runs);
        Assert.Contains(audit.Entries, entry => entry.Action == "approval.approved" && entry.Actor == "approver");
    }

    [Fact]
    public async Task FR34RejectedHeldCommandNeverRuns()
    {
        var shell = new RecordingShell();
        var service = Build(shell, new InMemoryAuditLog(), out _, new FakeClock(Start));
        var pending = (CommandPending)await service.SubmitAsync("hostname", ShellKind.PowerShell, Timeout, "alice", null, CancellationToken.None);

        var decided = await service.DecideAsync(pending.ApprovalId, approve: false, "approver", null, CancellationToken.None);

        Assert.Equal(ApprovalStatus.Rejected, decided.Status);
        Assert.Equal(0, shell.Runs);
    }

    [Fact]
    public async Task FR34ExpiredApprovalCannotBeApproved()
    {
        var shell = new RecordingShell();
        var clock = new FakeClock(Start);
        var service = Build(shell, new InMemoryAuditLog(), out _, clock);
        var pending = (CommandPending)await service.SubmitAsync("hostname", ShellKind.PowerShell, Timeout, "alice", null, CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(25));

        await Assert.ThrowsAsync<ApprovalStateException>(() =>
            service.DecideAsync(pending.ApprovalId, approve: true, "approver", null, CancellationToken.None));
        Assert.Equal(0, shell.Runs);
    }

    [Fact]
    public async Task FR34ApprovalCannotBeDecidedTwice()
    {
        var service = Build(new RecordingShell(), new InMemoryAuditLog(), out _, new FakeClock(Start));
        var pending = (CommandPending)await service.SubmitAsync("hostname", ShellKind.PowerShell, Timeout, "alice", null, CancellationToken.None);
        await service.DecideAsync(pending.ApprovalId, approve: true, "approver", null, CancellationToken.None);

        await Assert.ThrowsAsync<ApprovalStateException>(() =>
            service.DecideAsync(pending.ApprovalId, approve: true, "approver", null, CancellationToken.None));
    }

    [Fact]
    public async Task FR34UnknownApprovalIsReportedAsNotFound()
    {
        var service = Build(new RecordingShell(), new InMemoryAuditLog(), out _, new FakeClock(Start));

        await Assert.ThrowsAsync<ApprovalNotFoundException>(() =>
            service.DecideAsync(Guid.CreateVersion7(), approve: true, "approver", null, CancellationToken.None));
    }

    private static GovernedCommandService Build(
        RecordingShell shell,
        InMemoryAuditLog audit,
        out InMemoryApprovalStore approvals,
        TimeProvider clock)
    {
        approvals = new InMemoryApprovalStore();
        return new GovernedCommandService(shell, audit, approvals, clock);
    }

    private sealed class RecordingShell : IShellRunner
    {
        public int Runs { get; private set; }

        public Task<ShellResult> RunAsync(ShellRequest request, CancellationToken cancellationToken)
        {
            Runs++;
            return Task.FromResult(new ShellResult(ExitCode: 0, Stdout: string.Empty, Stderr: string.Empty, TimedOut: false));
        }
    }
}
