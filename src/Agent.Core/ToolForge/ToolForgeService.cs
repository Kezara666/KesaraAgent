using Agent.Core.Approvals;
using Agent.Core.Audit;
using Agent.Core.Governance;

namespace Agent.Core.ToolForge;

/// <summary>
/// Turns a missing capability into a promoted tool. Each step can stop the pipeline: name check,
/// generation, static checks, compile, tests, staging, and then the promotion gate (FR-5.1 to FR-5.4).
/// </summary>
public sealed class ToolForgeService(
    IToolGenerator generator,
    IToolCompiler compiler,
    IToolTestRunner testRunner,
    IToolRepository repository,
    IToolHistory history,
    IApprovalStore approvals,
    IAuditLog audit,
    TimeProvider clock)
{
    /// <summary>Generation attempts per capability gap before the forge gives up and asks a person.</summary>
    public const int MaxAttempts = 3;

    public const string ApprovalKind = "tool.promote";

    public async Task<ForgeOutcome> ForgeAsync(ToolSpec spec, string actor, CancellationToken cancellationToken)
    {
        spec.Validate();

        string? feedback = null;
        var reasons = new List<string>();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var tool = await generator.GenerateAsync(spec, feedback, cancellationToken).ConfigureAwait(false);
            if (tool is null)
            {
                feedback = "The generator returned no code.";
                reasons.Add($"attempt {attempt}: no code");
                continue;
            }

            var problems = await CheckAsync(spec, tool, cancellationToken).ConfigureAwait(false);
            if (problems.Count > 0)
            {
                feedback = string.Join(' ', problems);
                reasons.Add($"attempt {attempt}: {feedback}");
                continue;
            }

            await repository.StageAsync(spec, tool, cancellationToken).ConfigureAwait(false);

            if (PromotionTier.RequiresApproval(spec.SideEffect))
            {
                return await HoldForApprovalAsync(spec, actor, cancellationToken).ConfigureAwait(false);
            }

            return await PromoteAsync(spec, "Read-only tool passed all checks.", actor, cancellationToken)
                .ConfigureAwait(false);
        }

        await AuditAsync(actor, "tool.rejected", spec.Name, "Rejected", cancellationToken).ConfigureAwait(false);
        return new ForgeOutcome.Rejected(reasons);
    }

    /// <summary>Approve or reject a held promotion. Only a pending tool.promote approval can be decided here.</summary>
    public async Task<ForgeOutcome> DecideAsync(Guid approvalId, bool approve, string approver, CancellationToken cancellationToken)
    {
        var request = await approvals.GetAsync(approvalId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApprovalNotFoundException(approvalId);

        if (request.Kind != ApprovalKind || request.Status != ApprovalStatus.Pending)
        {
            throw new ApprovalStateException("The approval is not a pending tool promotion.");
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
            await approvals.UpdateAsync(request with { Status = ApprovalStatus.Rejected, DecidedAt = now }, cancellationToken)
                .ConfigureAwait(false);
            await AuditAsync(approver, "tool.promotion_rejected", request.Target, "Rejected", cancellationToken).ConfigureAwait(false);
            return new ForgeOutcome.Rejected(["rejected by approver"]);
        }

        await approvals.UpdateAsync(request with { Status = ApprovalStatus.Approved, DecidedAt = now }, cancellationToken)
            .ConfigureAwait(false);

        var spec = ToolSpecCodec.Decode(request.Payload);
        var outcome = await PromoteAsync(spec, "Promotion approved by a person.", approver, cancellationToken)
            .ConfigureAwait(false);

        await approvals.UpdateAsync(
                request with { Status = ApprovalStatus.Executed, DecidedAt = now, ResultSummary = "Promoted" },
                cancellationToken)
            .ConfigureAwait(false);

        return outcome;
    }

    private async Task<List<string>> CheckAsync(ToolSpec spec, GeneratedTool tool, CancellationToken cancellationToken)
    {
        var problems = StaticChecks.Violations(tool).ToList();
        if (problems.Count > 0)
        {
            return problems;
        }

        var compile = compiler.Compile(tool);
        if (!compile.Succeeded)
        {
            return compile.Errors.Take(5).ToList();
        }

        var tests = await testRunner.RunAsync(spec, tool, cancellationToken).ConfigureAwait(false);
        if (!tests.Passed)
        {
            return [$"Tests failed: {tests.Summary}"];
        }

        return problems;
    }

    private async Task<ForgeOutcome> HoldForApprovalAsync(ToolSpec spec, string actor, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var request = new ApprovalRequest(
            Guid.CreateVersion7(),
            ApprovalKind,
            spec.Name,
            ToolSpecCodec.Encode(spec),
            ApprovalStatus.Pending,
            now,
            now.Add(TimeSpan.FromHours(24)),
            DecidedAt: null,
            ResultSummary: null);

        await approvals.AddAsync(request, cancellationToken).ConfigureAwait(false);
        await AuditAsync(actor, "tool.promotion_requested", spec.Name, "Pending", cancellationToken).ConfigureAwait(false);
        return new ForgeOutcome.PendingApproval(request.Id, request.ExpiresAt);
    }

    private async Task<ForgeOutcome> PromoteAsync(ToolSpec spec, string reason, string actor, CancellationToken cancellationToken)
    {
        var path = await repository.PromoteAsync(spec, reason, cancellationToken).ConfigureAwait(false);
        var commit = await history.CommitAsync(path, $"feat(tools): FR-5.4 promote {spec.Name}", cancellationToken)
            .ConfigureAwait(false);
        await AuditAsync(actor, "tool.promoted", spec.Name, "Promoted", cancellationToken).ConfigureAwait(false);
        return new ForgeOutcome.Promoted(spec.Name, path, commit);
    }

    private Task AuditAsync(string actor, string action, string target, string result, CancellationToken cancellationToken) =>
        audit.AppendAsync(
            new AuditEntry(Guid.CreateVersion7(), clock.GetUtcNow(), actor, action, target, Tier: null, result, TraceId: null),
            cancellationToken);
}

/// <summary>Result of one forge request.</summary>
public abstract record ForgeOutcome
{
    public sealed record Promoted(string Name, string Path, string Commit) : ForgeOutcome;

    public sealed record PendingApproval(Guid ApprovalId, DateTimeOffset ExpiresAt) : ForgeOutcome;

    public sealed record Rejected(IReadOnlyList<string> Reasons) : ForgeOutcome;
}

/// <summary>Round-trips a spec through the approval payload, so the approved promotion matches what was reviewed.</summary>
public static class ToolSpecCodec
{
    public static string Encode(ToolSpec spec) => System.Text.Json.JsonSerializer.Serialize(spec);

    public static ToolSpec Decode(string payload)
    {
        var spec = System.Text.Json.JsonSerializer.Deserialize<ToolSpec>(payload)
            ?? throw new InvalidOperationException("The held tool could not be read.");
        spec.Validate();
        return spec;
    }
}
