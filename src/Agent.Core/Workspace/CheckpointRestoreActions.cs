using Agent.Core.Approvals;
using Agent.Core.Audit;
using Agent.Core.Governance;

namespace Agent.Core.Workspace;

/// <summary>
/// Restoring a checkpoint overwrites files, so it is a change and needs a person to approve it. The approval holds the
/// checkpoint id, and the restore runs once, only if it is approved before it expires.
/// </summary>
public sealed class CheckpointRestoreActions(
    IApprovalStore approvals,
    IWorkspaceCheckpoints checkpoints,
    IAuditLog audit,
    TimeProvider clock)
{
    public const string ApprovalKind = "workspace.restore";

    private static readonly TimeSpan ApprovalLifetime = TimeSpan.FromHours(24);
    private const int MaxSummaryCharacters = 256;

    public async Task<ApprovalRequest> RequestAsync(string checkpointId, string actor, CancellationToken cancellationToken)
    {
        var known = await checkpoints.ListAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);
        if (!known.Contains(checkpointId, StringComparer.Ordinal))
        {
            throw new ArgumentException("The checkpoint does not exist.", nameof(checkpointId));
        }

        var now = clock.GetUtcNow();
        var request = new ApprovalRequest(
            Guid.CreateVersion7(),
            ApprovalKind,
            "restore " + checkpointId[..12],
            checkpointId,
            ApprovalStatus.Pending,
            now,
            now.Add(ApprovalLifetime),
            DecidedAt: null,
            ResultSummary: null);

        await approvals.AddAsync(request, cancellationToken).ConfigureAwait(false);
        await AuditAsync(actor, "approval.requested", request.Target, "Pending", cancellationToken).ConfigureAwait(false);
        return request;
    }

    public async Task<ApprovalRequest> DecideAsync(Guid approvalId, bool approve, string approver, CancellationToken cancellationToken)
    {
        var request = await approvals.GetAsync(approvalId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApprovalNotFoundException(approvalId);

        if (request.Kind != ApprovalKind || request.Status != ApprovalStatus.Pending)
        {
            throw new ApprovalStateException("The approval is not a pending checkpoint restore.");
        }

        var now = clock.GetUtcNow();
        if (now > request.ExpiresAt)
        {
            await approvals.UpdateAsync(request with { Status = ApprovalStatus.Expired, DecidedAt = now }, cancellationToken)
                .ConfigureAwait(false);
            throw new ApprovalStateException("The approval has expired.");
        }

        if (!approve)
        {
            var rejected = request with { Status = ApprovalStatus.Rejected, DecidedAt = now };
            await approvals.UpdateAsync(rejected, cancellationToken).ConfigureAwait(false);
            await AuditAsync(approver, "approval.rejected", request.Target, "Rejected", cancellationToken).ConfigureAwait(false);
            return rejected;
        }

        var approved = request with { Status = ApprovalStatus.Approved, DecidedAt = now };
        await approvals.UpdateAsync(approved, cancellationToken).ConfigureAwait(false);
        await AuditAsync(approver, "approval.approved", request.Target, "Approved", cancellationToken).ConfigureAwait(false);

        string summary;
        ApprovalStatus status;
        try
        {
            await checkpoints.RestoreAsync(request.Payload, cancellationToken).ConfigureAwait(false);
            status = ApprovalStatus.Executed;
            summary = "Restored";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            status = ApprovalStatus.Failed;
            summary = "Failed: " + exception.GetType().Name;
        }

        var finished = approved with { Status = status, DecidedAt = now, ResultSummary = Truncate(summary) };
        await approvals.UpdateAsync(finished, cancellationToken).ConfigureAwait(false);
        await AuditAsync(approver, "workspace.restored", request.Target, finished.ResultSummary!, cancellationToken).ConfigureAwait(false);
        return finished;
    }

    private static string Truncate(string text) => text.Length <= MaxSummaryCharacters ? text : text[..MaxSummaryCharacters];

    private Task AuditAsync(string actor, string action, string target, string result, CancellationToken cancellationToken) =>
        audit.AppendAsync(
            new AuditEntry(Guid.CreateVersion7(), clock.GetUtcNow(), actor, action, target, Tier: null, result, null),
            cancellationToken);
}
