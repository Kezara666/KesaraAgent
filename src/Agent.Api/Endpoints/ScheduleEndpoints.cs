using Agent.Api.Auth;
using Agent.Core.Headless;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Agent.Api.Endpoints;

public static class ScheduleEndpoints
{
    private const int MaxPreviewCount = 10;

    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/schedules").WithTags("Schedules");

        group.MapPost("/preview", Preview)
            .RequireAuthorization(ApiPolicies.Read)
            .WithName("PreviewSchedule")
            .WithSummary("Show the next runs for a cron expression, before saving it.");

        group.MapPost("/", Create)
            .RequireAuthorization(ApiPolicies.Act)
            .WithName("CreateSchedule")
            .WithSummary("Save a schedule. Set confirm to true after reviewing the preview.");

        group.MapGet("/", List)
            .RequireAuthorization(ApiPolicies.Read)
            .WithName("ListSchedules");

        return app;
    }

    private static Results<Ok<PreviewResponse>, ProblemHttpResult> Preview(PreviewRequest request)
    {
        var (expression, error) = CronPreview.Parse(request.Cron ?? string.Empty);
        if (expression is null)
        {
            return TypedResults.Problem(title: "Invalid cron expression.", detail: error, statusCode: StatusCodes.Status400BadRequest);
        }

        var count = Math.Clamp(request.Count ?? 3, 1, MaxPreviewCount);
        var next = CronPreview.NextOccurrences(request.Cron!, DateTimeOffset.UtcNow, count);
        return TypedResults.Ok(new PreviewResponse(request.Cron!, next));
    }

    private static async Task<Results<Created<ScheduleResponse>, ProblemHttpResult>> Create(
        CreateScheduleRequest request,
        IScheduleStore store,
        CancellationToken cancellationToken)
    {
        if (request.Confirm != true)
        {
            return TypedResults.Problem(
                title: "Confirmation required.",
                detail: "Review the preview from POST /api/v1/schedules/preview, then send confirm: true.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var (expression, error) = CronPreview.Parse(request.Cron ?? string.Empty);
        if (expression is null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Prompt))
        {
            return TypedResults.Problem(
                title: "Invalid schedule.",
                detail: error ?? "Name and prompt are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var schedule = new Schedule(Guid.CreateVersion7(), request.Name, request.Cron!, request.Prompt, Enabled: true, LastRunAt: null);
        await store.AddAsync(schedule, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/schedules/{schedule.Id}", new ScheduleResponse(schedule.Id, schedule.Name, schedule.CronExpression, schedule.Enabled));
    }

    private static async Task<Ok<IReadOnlyList<ScheduleResponse>>> List(IScheduleStore store, CancellationToken cancellationToken)
    {
        var schedules = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<ScheduleResponse>>(
            schedules.Select(schedule => new ScheduleResponse(schedule.Id, schedule.Name, schedule.CronExpression, schedule.Enabled)).ToList());
    }
}

public sealed record PreviewRequest(string? Cron, int? Count);

public sealed record PreviewResponse(string Cron, IReadOnlyList<DateTimeOffset> NextRuns);

public sealed record CreateScheduleRequest(string? Name, string? Cron, string? Prompt, bool? Confirm);

public sealed record ScheduleResponse(Guid Id, string Name, string Cron, bool Enabled);
