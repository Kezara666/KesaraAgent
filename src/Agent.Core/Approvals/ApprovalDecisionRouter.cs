using Agent.Core.Agent;
using Agent.Core.Devices;
using Agent.Core.Governance;
using Agent.Core.ToolForge;
using Agent.Core.Workflows;
using Agent.Core.Workspace;

namespace Agent.Core.Approvals;

/// <summary>
/// The single entry point for a human decision on any held action. Each kind is decided by the service that owns it,
/// so a command, a workflow plan, and a tool promotion each keep their own rules and their own audit trail.
/// </summary>
public sealed class ApprovalDecisionRouter(
    IApprovalStore approvals,
    GovernedCommandService commands,
    WorkflowService workflows,
    ToolForgeService toolForge,
    DeviceCommandService? devices = null,
    AgentToolActions? agentTools = null,
    CheckpointRestoreActions? restores = null)
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
            case DeviceCommandService.ApprovalKind when devices is not null:
                await devices.DecideAsync(approvalId, approve, approver, cancellationToken).ConfigureAwait(false);
                break;
            case AgentLoop.ApprovalKind when agentTools is not null:
                await agentTools.DecideAsync(approvalId, approve, approver, cancellationToken).ConfigureAwait(false);
                break;
            case CheckpointRestoreActions.ApprovalKind when restores is not null:
                await restores.DecideAsync(approvalId, approve, approver, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ApprovalStateException($"No decision route exists for approvals of kind '{request.Kind}'.");
        }

        return await approvals.GetAsync(approvalId, cancellationToken).ConfigureAwait(false) ?? request;
    }
}
