using System.Text.Json;
using Agent.Core.Approvals;
using Agent.Core.Audit;
using Agent.Core.Shell;

namespace Agent.Core.Governance;

/// <summary>
/// The only route by which a shell command can run. Policy decides first. Denied commands are audited and refused.
/// Commands needing approval are held until a person approves them before expiry. Everything is audited (FR-3.4, FR-5.6).
/// </summary>
public sealed class GovernedCommandService(
    IShellRunner shell,
    IAuditLog audit,
    IApprovalStore approvals,
    TimeProvider clock)
{
    public static readonly TimeSpan ApprovalLifetime = TimeSpan.FromHours(24);

    private const int MaxSummaryLength = 256;

    public async Task<CommandSubmission> SubmitAsync(
        string command,
        ShellKind shellKind,
        TimeSpan timeout,
        string actor,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var decision = CommandPolicy.Evaluate(command);
        var target = ProgramOf(command);

        switch (decision.Outcome)
        {
            case CommandOutcome.Denied:
                await AuditAsync(actor, "command.denied", target, "Denied", traceId, cancellationToken).ConfigureAwait(false);
                throw new CommandNotAllowedException(decision);

            case CommandOutcome.RequiresApproval:
                return await HoldForApprovalAsync(command, shellKind, timeout, actor, traceId, cancellationToken)
                    .ConfigureAwait(false);

            default:
                var result = await shell.RunAsync(new ShellRequest(command, shellKind, timeout), cancellationToken)
                    .ConfigureAwait(false);
                await AuditAsync(actor, "command.executed", target, Summarize(result), traceId, cancellationToken)
                    .ConfigureAwait(false);
                return new CommandExecuted(result);
        }
    }

    public async Task<ApprovalRequest> DecideAsync(
        Guid approvalId,
        bool approve,
        string approver,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var request = await approvals.GetAsync(approvalId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApprovalNotFoundException(approvalId);

        // Only held commands run through this path. Plans and promotions are decided by their own services,
        // so approving them here would try to read them as commands.
        if (request.Kind != "command")
        {
            throw new ApprovalStateException("This approval is decided by its own workflow, not by the command path.");
        }

        if (request.Status != ApprovalStatus.Pending)
        {
            throw new ApprovalStateException($"The approval is {request.Status}, not pending.");
        }

        var now = clock.GetUtcNow();
        if (now > request.ExpiresAt)
        {
            var expired = request with { Status = ApprovalStatus.Expired, DecidedAt = now };
            await approvals.UpdateAsync(expired, cancellationToken).ConfigureAwait(false);
            await AuditAsync(approver, "approval.expired", request.Target, "Expired", traceId, cancellationToken)
                .ConfigureAwait(false);
            throw new ApprovalStateException("The approval has expired.");
        }

        if (!approve)
        {
            var rejected = request with { Status = ApprovalStatus.Rejected, DecidedAt = now };
            await approvals.UpdateAsync(rejected, cancellationToken).ConfigureAwait(false);
            await AuditAsync(approver, "approval.rejected", request.Target, "Rejected", traceId, cancellationToken)
                .ConfigureAwait(false);
            return rejected;
        }

        var approved = request with { Status = ApprovalStatus.Approved, DecidedAt = now };
        await approvals.UpdateAsync(approved, cancellationToken).ConfigureAwait(false);
        await AuditAsync(approver, "approval.approved", request.Target, "Approved", traceId, cancellationToken)
            .ConfigureAwait(false);

        var payload = JsonSerializer.Deserialize<CommandPayload>(request.Payload)
            ?? throw new InvalidOperationException("The held command could not be read.");

        var shellKind = Enum.Parse<ShellKind>(payload.Shell);
        var result = await shell.RunAsync(
                new ShellRequest(payload.Command, shellKind, TimeSpan.FromSeconds(payload.TimeoutSeconds)),
                cancellationToken)
            .ConfigureAwait(false);

        var finished = approved with
        {
            Status = result.TimedOut ? ApprovalStatus.Failed : ApprovalStatus.Executed,
            ResultSummary = Summarize(result),
        };
        await approvals.UpdateAsync(finished, cancellationToken).ConfigureAwait(false);
        await AuditAsync(approver, "command.executed", request.Target, finished.ResultSummary!, traceId, cancellationToken)
            .ConfigureAwait(false);

        return finished;
    }

    private async Task<CommandSubmission> HoldForApprovalAsync(
        string command,
        ShellKind shellKind,
        TimeSpan timeout,
        string actor,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var target = ProgramOf(command);
        var payload = JsonSerializer.Serialize(new CommandPayload(command, shellKind.ToString(), timeout.TotalSeconds));

        var request = new ApprovalRequest(
            Guid.CreateVersion7(),
            "command",
            target,
            payload,
            ApprovalStatus.Pending,
            now,
            now.Add(ApprovalLifetime),
            DecidedAt: null,
            ResultSummary: null);

        await approvals.AddAsync(request, cancellationToken).ConfigureAwait(false);
        await AuditAsync(actor, "approval.requested", target, "Pending", traceId, cancellationToken).ConfigureAwait(false);

        return new CommandPending(request.Id, request.ExpiresAt);
    }

    private Task AuditAsync(string actor, string action, string target, string result, string? traceId, CancellationToken cancellationToken) =>
        audit.AppendAsync(
            new AuditEntry(Guid.CreateVersion7(), clock.GetUtcNow(), actor, action, target, Tier: null, result, traceId),
            cancellationToken);

    private static string ProgramOf(string command)
    {
        var trimmed = command.Trim();
        return trimmed.Length == 0 ? "(empty)" : trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private static string Summarize(ShellResult result)
    {
        var summary = result.TimedOut
            ? "Timed out"
            : $"Exit {result.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}";
        return summary.Length <= MaxSummaryLength ? summary : summary[..MaxSummaryLength];
    }

    private sealed record CommandPayload(string Command, string Shell, double TimeoutSeconds);
}

public sealed class ApprovalNotFoundException(Guid id) : InvalidOperationException("The approval does not exist.")
{
    public Guid Id { get; } = id;
}

public sealed class ApprovalStateException(string message) : InvalidOperationException(message);
