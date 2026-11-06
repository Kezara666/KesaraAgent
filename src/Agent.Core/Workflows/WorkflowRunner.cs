using Agent.Core.Audit;

namespace Agent.Core.Workflows;

/// <summary>
/// Executes a validated plan step by step. Each step is stored when it finishes, so a restart resumes
/// without repeating completed work. External sends are guarded by an idempotency key. A failed step
/// is retried up to three times, and the run stops at the first step that cannot succeed (FR-9.4 to FR-9.6, FR-9.11, FR-9.12).
/// </summary>
public sealed class WorkflowRunner(
    IToolCatalog catalog,
    IToolInvoker invoker,
    IWorkflowStore store,
    IAuditLog audit,
    TimeProvider clock)
{
    public const int MaxAttemptsPerStep = 3;
    public const int MaxOutputCharacters = 8 * 1024;

    public async Task<WorkflowRun> RunAsync(string runId, CancellationToken cancellationToken)
    {
        var run = await store.GetRunAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The run does not exist.");

        if (run.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled)
        {
            return run;
        }

        var problems = PlanValidator.Validate(run.Plan, catalog);
        if (problems.Count > 0)
        {
            return await FinishAsync(run, RunStatus.Failed, failedStep: null, cancellationToken).ConfigureAwait(false);
        }

        run = run with { Status = RunStatus.Running };
        await store.SaveRunAsync(run, cancellationToken).ConfigureAwait(false);

        var done = (await store.GetStepsAsync(runId, cancellationToken).ConfigureAwait(false))
            .Where(record => record.Status == StepStatus.Completed)
            .ToDictionary(record => record.StepId, StringComparer.Ordinal);

        foreach (var step in run.Plan.Steps)
        {
            if (done.ContainsKey(step.Id))
            {
                continue;
            }

            // Re-read the status so a cancel from another request takes effect between steps.
            var current = await store.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);
            if (current?.Status == RunStatus.Cancelled)
            {
                return current;
            }

            var descriptor = catalog.Find(step.Tool)!;
            var key = $"{runId}:{step.Id}";
            var ok = await RunStepAsync(run, step, descriptor, key, done, cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                return await FinishAsync(run, RunStatus.Failed, step.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return await FinishAsync(run, RunStatus.Completed, failedStep: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RunStepAsync(
        WorkflowRun run,
        WorkflowStep step,
        ToolDescriptor descriptor,
        string key,
        Dictionary<string, StepRecord> done,
        CancellationToken cancellationToken)
    {
        var inputs = ResolveInputs(step, done);
        var attempts = 0;
        string? lastError = null;

        while (attempts < MaxAttemptsPerStep)
        {
            attempts++;
            try
            {
                var output = await invoker.InvokeAsync(step.Tool, inputs, key, cancellationToken).ConfigureAwait(false);
                var stored = Truncate(output);

                var record = new StepRecord(run.RunId, step.Id, StepStatus.Completed, stored, key, attempts);
                await store.SaveStepAsync(record, cancellationToken).ConfigureAwait(false);
                done[step.Id] = record;

                await AuditAsync(run, $"step.completed", $"{step.Id}:{step.Tool}", "Completed", cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Keep the type only. The message can echo prompt or tool content.
                lastError = exception.GetType().Name;
                await AuditAsync(run, "step.attempt_failed", $"{step.Id}:{step.Tool}", lastError, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var failed = new StepRecord(run.RunId, step.Id, StepStatus.Failed, lastError ?? string.Empty, key, attempts);
        await store.SaveStepAsync(failed, cancellationToken).ConfigureAwait(false);
        await AuditAsync(run, "step.failed", $"{step.Id}:{step.Tool}", "Failed", cancellationToken).ConfigureAwait(false);
        return false;
    }

    /// <summary>Resolves references to stored outputs. A reference to a missing output is a bug, so the step fails loudly.</summary>
    private static Dictionary<string, string> ResolveInputs(WorkflowStep step, Dictionary<string, StepRecord> done)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in step.Inputs)
        {
            if (!value.StartsWith(PlanValidator.ReferencePrefix, StringComparison.Ordinal))
            {
                resolved[name] = value;
                continue;
            }

            var target = value[PlanValidator.ReferencePrefix.Length..];
            if (!done.TryGetValue(target, out var source))
            {
                throw new InvalidOperationException($"Step {step.Id} needs the output of {target}, which is not available.");
            }

            resolved[name] = source.Output;
        }

        return resolved;
    }

    private async Task<WorkflowRun> FinishAsync(WorkflowRun run, RunStatus status, string? failedStep, CancellationToken cancellationToken)
    {
        var finished = run with { Status = status, FailedStep = failedStep };
        await store.SaveRunAsync(finished, cancellationToken).ConfigureAwait(false);
        await AuditAsync(run, "run.finished", run.RunId, status.ToString(), cancellationToken).ConfigureAwait(false);
        return finished;
    }

    private Task AuditAsync(WorkflowRun run, string action, string target, string result, CancellationToken cancellationToken) =>
        audit.AppendAsync(
            new AuditEntry(Guid.CreateVersion7(), clock.GetUtcNow(), "workflow", action, target, Tier: null, result, run.TraceId),
            cancellationToken);

    private static string Truncate(string output) =>
        output.Length <= MaxOutputCharacters ? output : output[..MaxOutputCharacters];
}
