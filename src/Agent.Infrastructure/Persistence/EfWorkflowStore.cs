using System.Text.Json;
using Agent.Core.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Persistence;

/// <summary>Durable run and step state (FR-9.6). Runs resume from the stored steps after a restart.</summary>
public sealed class EfWorkflowStore(IDbContextFactory<AgentDbContext> factory) : IWorkflowStore
{
    public async Task SaveRunAsync(WorkflowRun run, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = ToRow(run);
        var existing = await db.WorkflowRuns.FindAsync([run.RunId], cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.WorkflowRuns.Add(row);
        }
        else
        {
            existing.PlanJson = row.PlanJson;
            existing.Status = row.Status;
            existing.TraceId = row.TraceId;
            existing.FailedStep = row.FailedStep;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.WorkflowRuns.AsNoTracking().FirstOrDefaultAsync(run => run.RunId == runId, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToRecord(row);
    }

    public async Task SaveStepAsync(StepRecord record, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.WorkflowSteps.FindAsync([record.RunId, record.StepId], cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.WorkflowSteps.Add(new WorkflowStepRow
            {
                RunId = record.RunId,
                StepId = record.StepId,
                Status = record.Status.ToString(),
                Output = record.Output,
                IdempotencyKey = record.IdempotencyKey,
                Attempts = record.Attempts,
            });
        }
        else
        {
            existing.Status = record.Status.ToString();
            existing.Output = record.Output;
            existing.IdempotencyKey = record.IdempotencyKey;
            existing.Attempts = record.Attempts;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StepRecord>> GetStepsAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.WorkflowSteps.AsNoTracking()
            .Where(step => step.RunId == runId)
            .OrderBy(step => step.StepId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(row => new StepRecord(
            row.RunId,
            row.StepId,
            Enum.Parse<StepStatus>(row.Status),
            row.Output,
            row.IdempotencyKey,
            row.Attempts)).ToList();
    }

    private static WorkflowRunRow ToRow(WorkflowRun run) => new()
    {
        RunId = run.RunId,
        PlanJson = JsonSerializer.Serialize(run.Plan),
        Status = run.Status.ToString(),
        TraceId = run.TraceId,
        FailedStep = run.FailedStep,
    };

    private static WorkflowRun ToRecord(WorkflowRunRow row) => new(
        row.RunId,
        JsonSerializer.Deserialize<WorkflowPlan>(row.PlanJson) ?? throw new InvalidOperationException("The stored plan could not be read."),
        Enum.Parse<RunStatus>(row.Status),
        row.TraceId,
        row.FailedStep);
}
