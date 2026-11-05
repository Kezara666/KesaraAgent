using Agent.Api.Auth;
using Agent.Core.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Agent.Api.Endpoints;

public static class TokenEndpoints
{
    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tokens")
            .WithTags("Tokens")
            .RequireAuthorization(ApiPolicies.Admin);

        group.MapPost("/", Create)
            .WithName("CreateToken")
            .WithSummary("Create a client token with the given scopes. The raw token is shown once.");

        group.MapGet("/", List)
            .WithName("ListTokens")
            .WithSummary("List tokens by name, scopes, and state. Raw tokens and hashes are never shown.");

        group.MapPost("/{id:guid}/revoke", Revoke)
            .WithName("RevokeToken")
            .WithSummary("Revoke a token. It is refused from the next request on.");

        return app;
    }

    private static async Task<Results<Created<CreatedTokenResponse>, ProblemHttpResult>> Create(
        CreateTokenRequest request,
        IApiTokenStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        try
        {
            var (record, raw) = await ApiTokenService.CreateAsync(
                store,
                request.Name ?? string.Empty,
                request.Scopes ?? [],
                clock.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);

            return TypedResults.Created(
                $"/api/v1/tokens/{record.Id}",
                new CreatedTokenResponse(record.Id, record.Name, record.Scopes, raw));
        }
        catch (ArgumentException exception)
        {
            return TypedResults.Problem(title: "Invalid token request.", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<Ok<IReadOnlyList<TokenResponse>>> List(IApiTokenStore store, CancellationToken cancellationToken)
    {
        var tokens = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<TokenResponse>>(tokens
            .Select(token => new TokenResponse(token.Id, token.Name, token.Scopes, token.CreatedAt, token.RevokedAt))
            .ToList());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Revoke(
        Guid id,
        IApiTokenStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.RevokeAsync(id, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException)
        {
            return TypedResults.Problem(title: "Token not found.", statusCode: StatusCodes.Status404NotFound);
        }
    }
}

public sealed record CreateTokenRequest(string? Name, IReadOnlyList<string>? Scopes);

public sealed record CreatedTokenResponse(Guid Id, string Name, string Scopes, string Token);

public sealed record TokenResponse(Guid Id, string Name, string Scopes, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);
