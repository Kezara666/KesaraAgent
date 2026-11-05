using System.Text.Json;
using Agent.Core.Approvals;
using Agent.Core.Audit;
using Agent.Core.Governance;

namespace Agent.Core.Workflows;

/// <summary>
/// Accepts plans, holds plans that can change things outside the agent until a person approves them,
/// and runs approved plans. A plan is reviewed exactly as it will run (FR-9.7, FR-9.12).
/// </summary>
public sealed class WorkflowService(
    IToolCatalog catalog,
    IWorkflowStore store,
    IApprovalStore approvals,
    IAuditLog audit,
    WorkflowRunner runner,
    TimeProvider clock)
{
    public const string ApprovalKind = "workflow.plan";

    private static readonly TimeSpan ApprovalLifetime = TimeSpan.FromHours(24);

    public async Task<WorkflowRun> SubmitAsync(WorkflowPlan plan, string traceId, CancellationToken cancellationToken)
    {
        var problems = PlanValidator.Validate(plan, catalog);
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join(' ', problems), nameof(plan));
        }

        var needsApproval = PlanValidator.NeedsApproval(plan, catalog);
        var run = new WorkflowRun(
            Guid.CreateVersion7().ToString("N"),
            plan,
            needsApproval ? RunStatus.WaitingApproval : RunStatus.Planned,
            traceId,
            FailedStep: null);

        await store.SaveRunAsync(run, cancellationToken).ConfigureAwait(false);

        if (needsApproval)
        {
            var now = clock.GetUtcNow();
            await approvals.AddAsync(
                    new ApprovalRequest(
                        Guid.CreateVersion7(),
                        ApprovalKind,
                        run.RunId,
                        JsonSerializer.Serialize(plan.Steps),
                        ApprovalStatus.Pending,
                        now,
                        now.Add(ApprovalLifetime),
                        DecidedAt: null,
                        ResultSummary: null),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            return await runner.RunAsync(run.RunId, cancellationToken).ConfigureAwait(false);
        }

        await AuditAsync(run, "plan.waiting_approval", run.RunId, cancellationToken).ConfigureAwait(false);
        return run;
    }

    /// <summary>Approves or rejects a held plan. Approval runs it; rejection cancels it, and nothing runs.</summary>
    public async Task<WorkflowRun> DecideAsync(Guid approvalId, bool approve, string approver, CancellationToken cancellationToken)
    {
        var request = await approvals.GetAsync(approvalId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApprovalNotFoundException(approvalId);

        if (request.Kind != ApprovalKind || request.Status != ApprovalStatus.Pending)
        {
            throw new ApprovalStateException("The approval is not a pending plan.");
        }

        var now = clock.GetUtcNow();
        if (now > request.ExpiresAt)
        {
            await approvals.UpdateAsync(request with { Status = ApprovalStatus.Expired, DecidedAt = now }, cancellationToken)
                .ConfigureAwait(false);
            throw new ApprovalStateException("The approval has expired.");
        }

        var run = await store.GetRunAsync(request.Target, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The plan no longer exists.");

        if (!approve)
        {
            await approvals.UpdateAsync(request with { Status = ApprovalStatus.Rejected, DecidedAt = now }, cancellationToken)
                .ConfigureAwait(false);
            var cancelled = run with { Status = RunStatus.Cancelled };
            await store.SaveRunAsync(cancelled, cancellationToken).ConfigureAwait(false);
            return cancelled;
        }

        await approvals.UpdateAsync(request with { Status = ApprovalStatus.Approved, DecidedAt = now }, cancellationToken)
            .ConfigureAwait(false);
        await store.SaveRunAsync(run with { Status = RunStatus.Planned }, cancellationToken).ConfigureAwait(false);
        await AuditAsync(run, "plan.approved", approver, cancellationToken).ConfigureAwait(false);

        var finished = await runner.RunAsync(run.RunId, cancellationToken).ConfigureAwait(false);
        await approvals.UpdateAsync(
                request with { Status = ApprovalStatus.Executed, DecidedAt = now, ResultSummary = finished.Status.ToString() },
                cancellationToken)
            .ConfigureAwait(false);
        return finished;
    }

    /// <summary>Cancels a run that has not finished. Steps already completed are kept.</summary>
    public async Task<WorkflowRun?> CancelAsync(string runId, string actor, CancellationToken cancellationToken)
    {
        var run = await store.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled)
        {
            return run;
        }

        var cancelled = run with { Status = RunStatus.Cancelled };
        await store.SaveRunAsync(cancelled, cancellationToken).ConfigureAwait(false);
        await AuditAsync(run, "run.cancelled", actor, cancellationToken).ConfigureAwait(false);
        return cancelled;
    }

    private Task AuditAsync(WorkflowRun run, string action, string target, CancellationToken cancellationToken) =>
        audit.AppendAsync(
            new AuditEntry(Guid.CreateVersion7(), clock.GetUtcNow(), "workflow", action, target, Tier: null, run.Status.ToString(), run.TraceId),
            cancellationToken);
}
