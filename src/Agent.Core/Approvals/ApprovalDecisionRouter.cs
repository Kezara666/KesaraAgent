using Agent.Core.Governance;
using Agent.Core.ToolForge;
using Agent.Core.Workflows;

namespace Agent.Core.Approvals;

/// <summary>
/// The single entry point for a human decision on any held action. Each kind is decided by the service that owns it,
/// so a command, a workflow plan, and a tool promotion each keep their own rules and their own audit trail.
/// </summary>
public sealed class ApprovalDecisionRouter(
    IApprovalStore approvals,
    GovernedCommandService commands,
    WorkflowService workflows,
    ToolForgeService toolForge)
{
    public const string CommandKind = "command";

    public async Task<ApprovalRequest> DecideAsync(
        Guid approvalId,
        bool approve,
        string approver,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var request = await approvals.GetAsync(approvalId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApprovalNotFoundException(approvalId);

        switch (request.Kind)
        {
            case CommandKind:
                await commands.DecideAsync(approvalId, approve, approver, traceId, cancellationToken).ConfigureAwait(false);
                break;
            case WorkflowService.ApprovalKind:
                await workflows.DecideAsync(approvalId, approve, approver, cancellationToken).ConfigureAwait(false);
                break;
            case ToolForgeService.ApprovalKind:
                await toolForge.DecideAsync(approvalId, approve, approver, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ApprovalStateException($"No decision route exists for approvals of kind '{request.Kind}'.");
        }

        return await approvals.GetAsync(approvalId, cancellationToken).ConfigureAwait(false) ?? request;
    }
}
