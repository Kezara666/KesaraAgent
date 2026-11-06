using System.Diagnostics;
using Agent.Api.Auth;
using Agent.Core.Workflows;

namespace Agent.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workflows").WithTags("Workflows");

        group.MapPost("/", SubmitAsync)
            .RequireAuthorization(ApiPolicies.Act)
            .WithName("SubmitWorkflow")
            .WithSummary("Submit a plan. Plans that use write or external tools wait for approval before any step runs.");

        group.MapGet("/{runId}", GetAsync)
            .RequireAuthorization(ApiPolicies.Read)
            .WithName("GetWorkflow")
            .WithSummary("Get a run, its status, and the outcome of each step.");

        group.MapPost("/{runId}/cancel", CancelAsync)
            .RequireAuthorization(ApiPolicies.Act)
            .WithName("CancelWorkflow")
            .WithSummary("Cancel a run that has not finished. Completed steps are kept.");

        return app;
    }

    private static async Task<IResult> SubmitAsync(
        WorkflowRequest request,
        WorkflowService service,
        IWorkflowStore store,
        CancellationToken cancellationToken)
    {
        if (request.Steps is null || request.Steps.Count == 0)
        {
            return Results.Problem(title: "A plan needs at least one step.", statusCode: StatusCodes.Status400BadRequest);
        }

        var plan = new WorkflowPlan(request.Steps
            .Select(step => new WorkflowStep(step.Id ?? string.Empty, step.Tool ?? string.Empty, step.Inputs ?? new Dictionary<string, string>()))
            .ToList());

        try
        {
            var run = await service.SubmitAsync(plan, Activity.Current?.TraceId.ToString() ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
            var steps = await store.GetStepsAsync(run.RunId, cancellationToken).ConfigureAwait(false);
            var response = WorkflowRunResponse.From(run, steps);
            return run.Status == RunStatus.WaitingApproval
                ? Results.Accepted($"/api/v1/workflows/{run.RunId}", response)
                : Results.Ok(response);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(title: "The plan was refused.", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> GetAsync(
        string runId,
        IWorkflowStore store,
        CancellationToken cancellationToken)
    {
        var run = await store.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return Results.Problem(title: "Workflow run not found.", statusCode: StatusCodes.Status404NotFound);
        }

        var steps = await store.GetStepsAsync(runId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(WorkflowRunResponse.From(run, steps));
    }

    private static async Task<IResult> CancelAsync(
        string runId,
        HttpContext httpContext,
        WorkflowService service,
        IWorkflowStore store,
        CancellationToken cancellationToken)
    {
        var run = await service.CancelAsync(runId, httpContext.User.Identity?.Name ?? "anonymous", cancellationToken)
            .ConfigureAwait(false);
        if (run is null)
        {
            return Results.Problem(
                title: "The run cannot be cancelled.",
                detail: "It is not found or has already finished.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var steps = await store.GetStepsAsync(runId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(WorkflowRunResponse.From(run, steps));
    }
}

public sealed record WorkflowRequest(IReadOnlyList<WorkflowStepRequest>? Steps);

public sealed record WorkflowStepRequest(string? Id, string? Tool, IReadOnlyDictionary<string, string>? Inputs);

public sealed record WorkflowRunResponse(
    string RunId,
    string Status,
    string? FailedStep,
    IReadOnlyList<WorkflowStepResponse> Steps)
{
    public static WorkflowRunResponse From(WorkflowRun run, IReadOnlyList<StepRecord> steps) => new(
        run.RunId,
        run.Status.ToString(),
        run.FailedStep,
        steps.Select(step => new WorkflowStepResponse(step.StepId, step.Status.ToString(), step.Attempts, step.Output)).ToList());
}

public sealed record WorkflowStepResponse(string StepId, string Status, int Attempts, string Output);
