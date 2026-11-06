using Agent.Api.Auth;
using Agent.Core.Audit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Endpoints;

public static class AuditEndpoints
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/audit", ListAsync)
            .WithTags("Audit")
            .RequireAuthorization(ApiPolicies.Admin)
            .WithName("ListAuditEntries")
            .WithSummary("List recent audit entries, newest first. Read-only.");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<AuditEntry>>> ListAsync(
        IAuditLog audit,
        CancellationToken cancellationToken,
        [FromQuery] int? limit = null)
    {
        var entries = await audit.ListRecentAsync(Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(entries);
    }
}
