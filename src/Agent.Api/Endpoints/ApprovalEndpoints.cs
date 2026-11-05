using System.Diagnostics;
using Agent.Api.Auth;
using Agent.Core.Approvals;
using Agent.Core.Governance;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Endpoints;

public static class ApprovalEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/approvals")
            .WithTags("Approvals")
            .RequireAuthorization(ApiPolicies.Read);

        group.MapGet("/", ListAsync)
            .WithName("ListApprovals")
            .WithSummary("List approval requests, optionally filtered by status.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetApproval")
            .WithSummary("Get one approval request and its outcome.");

        group.MapPost("/{id:guid}/approve", ApproveAsync)
            .RequireAuthorization(ApiPolicies.Admin)
            .WithName("ApproveRequest")
            .WithSummary("Approve a held action. It runs once, if it has not expired.");

        group.MapPost("/{id:guid}/reject", RejectAsync)
            .RequireAuthorization(ApiPolicies.Admin)
            .WithName("RejectRequest")
            .WithSummary("Reject a held action. It never runs.");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<ApprovalResponse>>> ListAsync(
        [FromQuery] string? status,
        [FromQuery] int? limit,
        IApprovalStore store,
        CancellationToken cancellationToken)
    {
        ApprovalStatus? filter = Enum.TryParse<ApprovalStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;
        var rows = await store.ListAsync(filter, Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<ApprovalResponse>>(rows.Select(ApprovalResponse.From).ToList());
    }

    private static async Task<Results<Ok<ApprovalResponse>, ProblemHttpResult>> GetAsync(
        Guid id,
        IApprovalStore store,
        CancellationToken cancellationToken)
    {
        var request = await store.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return request is null
            ? TypedResults.Problem(title: "Approval not found.", statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(ApprovalResponse.From(request));
    }

    private static Task<IResult> ApproveAsync(Guid id, HttpContext httpContext, GovernedCommandService service, CancellationToken cancellationToken) =>
        Decide(id, approve: true, httpContext, service, cancellationToken);

    private static Task<IResult> RejectAsync(Guid id, HttpContext httpContext, GovernedCommandService service, CancellationToken cancellationToken) =>
        Decide(id, approve: false, httpContext, service, cancellationToken);

    private static async Task<IResult> Decide(
        Guid id,
        bool approve,
        HttpContext httpContext,
        GovernedCommandService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.DecideAsync(
                    id,
                    approve,
                    httpContext.User.Identity?.Name ?? "anonymous",
                    Activity.Current?.TraceId.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(ApprovalResponse.From(result));
        }
        catch (ApprovalNotFoundException)
        {
            return Results.Problem(title: "Approval not found.", statusCode: StatusCodes.Status404NotFound);
        }
        catch (ApprovalStateException exception)
        {
            return Results.Problem(title: "The approval cannot be decided.", detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}

/// <summary>Public view of an approval. The held payload is omitted because it can contain command text.</summary>
public sealed record ApprovalResponse(
    Guid Id,
    string Kind,
    string Target,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt,
    string? ResultSummary)
{
    public static ApprovalResponse From(ApprovalRequest request) => new(
        request.Id,
        request.Kind,
        request.Target,
        request.Status.ToString(),
        request.CreatedAt,
        request.ExpiresAt,
        request.DecidedAt,
        request.ResultSummary);
}
