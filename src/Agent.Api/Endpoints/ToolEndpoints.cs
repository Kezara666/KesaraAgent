using Agent.Api.Auth;
using Agent.Core.ToolForge;
using Agent.Core.Workflows;

namespace Agent.Api.Endpoints;

public static class ToolEndpoints
{
    public static IEndpointRouteBuilder MapToolEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tools").WithTags("Tools");

        group.MapPost("/forge", ForgeAsync)
            .RequireAuthorization(ApiPolicies.Act)
            .WithName("ForgeTool")
            .WithSummary("Generate, compile, test, and promote a tool. Read-only tools promote automatically; others wait for approval.");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(ApiPolicies.Read)
            .WithName("ListTools")
            .WithSummary("Every tool the planner can use, with its side effect and required inputs.");

        return app;
    }

    private static IResult ListAsync(IToolListing listing) =>
        Results.Ok(listing.List()
            .Select(tool => new ToolResponse(tool.Name, tool.SideEffect.ToString(), tool.RequiredInputs))
            .ToList());

    private static async Task<IResult> ForgeAsync(
        ForgeRequest request,
        HttpContext httpContext,
        ToolForgeService service,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SideEffect>(request.SideEffect, ignoreCase: true, out var sideEffect))
        {
            return Results.Problem(
                title: "The side effect is not recognised.",
                detail: "Use ReadOnly, Write, or ExternalSend.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var spec = new ToolSpec(
                request.Name ?? string.Empty,
                request.Domain ?? string.Empty,
                request.Purpose ?? string.Empty,
                sideEffect,
                request.NeedsNetwork);
            var actor = httpContext.User.Identity?.Name ?? "anonymous";
            var outcome = await service.ForgeAsync(spec, actor, cancellationToken).ConfigureAwait(false);

            return outcome switch
            {
                ForgeOutcome.Promoted promoted => Results.Ok(
                    new ForgeResponse("Promoted", promoted.Name, promoted.Path, promoted.Commit, null, [])),
                ForgeOutcome.PendingApproval pending => Results.Accepted(
                    $"/api/v1/approvals/{pending.ApprovalId}",
                    new ForgeResponse("PendingApproval", spec.Name, null, null, pending.ApprovalId, [])),
                ForgeOutcome.Rejected rejected => Results.Problem(
                    title: "The generated tool was rejected.",
                    detail: string.Join(' ', rejected.Reasons),
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.Problem(title: "The forge returned an unknown outcome.", statusCode: StatusCodes.Status500InternalServerError),
            };
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(title: "The tool request was refused.", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}

public sealed record ToolResponse(string Name, string SideEffect, IReadOnlyList<string> RequiredInputs);

public sealed record ForgeRequest(string? Name, string? Domain, string? Purpose, string? SideEffect, bool NeedsNetwork);

public sealed record ForgeResponse(
    string Status,
    string? Name,
    string? Path,
    string? Commit,
    Guid? ApprovalId,
    IReadOnlyList<string> Reasons);
