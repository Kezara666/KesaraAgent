using Agent.Core.Approvals;
using Agent.Core.Governance;
using Agent.Core.Tests.Fakes;
using Agent.Core.Workspace;
using Xunit;

namespace Agent.Core.Tests.Workspace;

public sealed class CheckpointRestoreActionsTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
    private const string KnownId = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task FR35UnknownCheckpointCannotBeRequested()
    {
        var actions = new CheckpointRestoreActions(new InMemoryApprovalStore(), new FakeCheckpoints(), new InMemoryAuditLog(), new FakeClock(Start));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            actions.RequestAsync("ffffffffffffffffffffffffffffffffffffffff", "alice", CancellationToken.None));
    }

    [Fact]
    public async Task FR35ApprovedRestoreRunsExactlyOnce()
    {
        var approvals = new InMemoryApprovalStore();
        var checkpoints = new FakeCheckpoints();
        var actions = new CheckpointRestoreActions(approvals, checkpoints, new InMemoryAuditLog(), new FakeClock(Start));
        var request = await actions.RequestAsync(KnownId, "alice", CancellationToken.None);
        Assert.Equal(0, checkpoints.Restores);

        var decided = await actions.DecideAsync(request.Id, approve: true, "approver", CancellationToken.None);

        Assert.Equal(ApprovalStatus.Executed, decided.Status);
        Assert.Equal(1, checkpoints.Restores);
        Assert.Equal(KnownId, checkpoints.LastRestored);
        await Assert.ThrowsAsync<ApprovalStateException>(() =>
            actions.DecideAsync(request.Id, approve: true, "approver", CancellationToken.None));
    }

    [Fact]
    public async Task FR35RejectedRestoreNeverRuns()
    {
        var checkpoints = new FakeCheckpoints();
        var actions = new CheckpointRestoreActions(new InMemoryApprovalStore(), checkpoints, new InMemoryAuditLog(), new FakeClock(Start));
        var request = await actions.RequestAsync(KnownId, "alice", CancellationToken.None);

        var decided = await actions.DecideAsync(request.Id, approve: false, "approver", CancellationToken.None);

        Assert.Equal(ApprovalStatus.Rejected, decided.Status);
        Assert.Equal(0, checkpoints.Restores);
    }

    private sealed class FakeCheckpoints : IWorkspaceCheckpoints
    {
        public int Restores { get; private set; }

        public string? LastRestored { get; private set; }

        public Task<string?> CreateAsync(string label, CancellationToken cancellationToken) => Task.FromResult<string?>(KnownId);

        public Task<IReadOnlyList<string>> ListAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([KnownId]);

        public Task RestoreAsync(string checkpointId, CancellationToken cancellationToken)
        {
            Restores++;
            LastRestored = checkpointId;
            return Task.CompletedTask;
        }
    }
}
