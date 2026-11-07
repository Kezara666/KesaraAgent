using Agent.Api.Auth;
using Agent.Core.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Endpoints;

public static class TaskEndpoints
{
    private const int MaxIdempotencyKeyLength = 128;
    private const int DefaultListLimit = 50;
    private const int MaxListLimit = 200;

    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tasks")
            .WithTags("Tasks")
            .RequireAuthorization(ApiPolicies.Read);

        group.MapPost("/", SubmitAsync)
            .RequireAuthorization(ApiPolicies.Act)
            .WithName("SubmitTask")
            .WithSummary("Submit a task. Send Idempotency-Key to make retries safe.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetTask")
            .WithSummary("Get a task and its current status.");

        group.MapGet("/", ListAsync)
            .WithName("ListTasks")
            .WithSummary("List recent tasks, newest first.");

        return app;
    }

    private static async Task<IResult> SubmitAsync(
        SubmitTaskRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        TaskService service,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is { Length: > MaxIdempotencyKeyLength })
        {
            return Results.Problem(
                title: "Invalid Idempotency-Key",
                detail: $"Idempotency-Key must be {MaxIdempotencyKeyLength} characters or fewer.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var result = await service.SubmitAsync(request.Prompt ?? string.Empty, idempotencyKey, cancellationToken, request.ConversationId)
                .ConfigureAwait(false);
            var body = TaskResponse.From(result.Task);

            return result.Created
                ? Results.Accepted($"/api/v1/tasks/{body.Id}", body)
                : Results.Ok(body);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                title: "Invalid task",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<Results<Ok<TaskResponse>, ProblemHttpResult>> GetAsync(
        Guid id,
        TaskService service,
        CancellationToken cancellationToken)
    {
        var task = await service.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return TypedResults.Problem(
                title: "Task not found",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(TaskResponse.From(task));
    }

    private static async Task<Ok<IReadOnlyList<TaskResponse>>> ListAsync(
        TaskService service,
        CancellationToken cancellationToken,
        [FromQuery] int? limit = null)
    {
        var effective = Math.Clamp(limit ?? DefaultListLimit, 1, MaxListLimit);
        var tasks = await service.ListAsync(effective, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<TaskResponse>>(tasks.Select(TaskResponse.From).ToList());
    }
}

/// <summary>Request body for task submission.</summary>
/// <param name="Prompt">The request to run. Required, up to 4000 characters.</param>
/// <param name="ConversationId">Optional. Tasks with the same id share context. Omit for a standalone task.</param>
public sealed record SubmitTaskRequest(string? Prompt, Guid? ConversationId = null);

/// <summary>Public view of a task. Mirrors the Core record without exposing internal types.</summary>
public sealed record TaskResponse(
    Guid Id,
    string Status,
    string Prompt,
    string? Result,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ConversationId = null)
{
    public static TaskResponse From(AgentTask task) => new(
        task.Id,
        task.Status.ToString(),
        task.Prompt,
        task.Result,
        task.Error,
        task.CreatedAt,
        task.UpdatedAt,
        task.ConversationId);
}
