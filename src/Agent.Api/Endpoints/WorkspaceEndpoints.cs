using System.Diagnostics;
using Agent.Api.Auth;
using Agent.Core.Governance;
using Agent.Core.Shell;
using Agent.Core.Workspace;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Endpoints;

public static class WorkspaceEndpoints
{
    private const int DefaultSearchLimit = 50;
    private const int DefaultTimeoutSeconds = 30;
    private const int MaxTimeoutSeconds = 120;

    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspace")
            .WithTags("Workspace")
            .RequireAuthorization(ApiPolicies.Read);

        group.MapGet("/files/{*path}", ReadFileAsync)
            .WithName("ReadWorkspaceFile")
            .WithSummary("Read a text file inside the workspace.");

        group.MapGet("/search", SearchAsync)
            .WithName("SearchWorkspace")
            .WithSummary("Search text files for a phrase, case-insensitive.");

        group.MapPost("/edits", EditAsync)
            .RequireAuthorization(ApiPolicies.Act)
            .WithName("EditWorkspaceFile")
            .WithSummary("Replace one exact, unique match in a file and return the diff.");

        group.MapPost("/commands", RunCommandAsync)
            .RequireAuthorization(ApiPolicies.Act)
            .WithName("RunWorkspaceCommand")
            .WithSummary("Run an allowed command in the workspace. Denied commands are refused with 403.");

        group.MapGet("/checkpoints", ListCheckpointsAsync)
            .WithName("ListWorkspaceCheckpoints")
            .WithSummary("Recent workspace checkpoints, newest first.");

        group.MapPost("/checkpoints/{id}/restore", RestoreCheckpointAsync)
            .RequireAuthorization(ApiPolicies.Act)
            .WithName("RestoreWorkspaceCheckpoint")
            .WithSummary("Ask to restore a checkpoint. The restore runs only after a person approves it.");

        return app;
    }

    private static async Task<IResult> ListCheckpointsAsync(IWorkspaceCheckpoints checkpoints, CancellationToken cancellationToken) =>
        Results.Ok(await checkpoints.ListAsync(50, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RestoreCheckpointAsync(
        string id,
        HttpContext httpContext,
        CheckpointRestoreActions restores,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await restores.RequestAsync(id, httpContext.User.Identity?.Name ?? "anonymous", cancellationToken)
                .ConfigureAwait(false);
            return Results.Accepted(
                $"/api/v1/approvals/{request.Id}",
                new { status = "PendingApproval", approvalId = request.Id });
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(title: "The checkpoint cannot be restored.", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static Task<IResult> ReadFileAsync(string path, WorkspaceService service, CancellationToken cancellationToken) =>
        Guard(async () =>
        {
            var text = await service.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            return Results.Text(text, "text/plain");
        });

    private static Task<IResult> SearchAsync(
        WorkspaceService service,
        [FromQuery] string? query,
        [FromQuery] int? limit,
        CancellationToken cancellationToken) =>
        Guard(async () =>
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.Problem(title: "Query is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var hits = await service.SearchAsync(query, limit ?? DefaultSearchLimit, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(hits);
        });

    private static Task<IResult> EditAsync(EditRequest request, WorkspaceService service, CancellationToken cancellationToken) =>
        Guard(async () =>
        {
            if (string.IsNullOrEmpty(request.Path) || string.IsNullOrEmpty(request.Search) || request.Replace is null)
            {
                return Results.Problem(
                    title: "Path, search, and replace are required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.EditAsync(request.Path, request.Search, request.Replace, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        });

    private static Task<IResult> RunCommandAsync(
        CommandRequest request,
        HttpContext httpContext,
        GovernedCommandService service,
        CancellationToken cancellationToken) =>
        Guard(async () =>
        {
            if (!Enum.TryParse<ShellKind>(request.Shell, ignoreCase: true, out var shell))
            {
                return Results.Problem(
                    title: "Shell must be 'powershell' or 'bash'.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var seconds = Math.Clamp(request.TimeoutSeconds ?? DefaultTimeoutSeconds, 1, MaxTimeoutSeconds);
            var submission = await service.SubmitAsync(
                    request.Command ?? string.Empty,
                    shell,
                    TimeSpan.FromSeconds(seconds),
                    httpContext.User.Identity?.Name ?? "anonymous",
                    Activity.Current?.TraceId.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);

            return submission switch
            {
                CommandExecuted executed => Results.Ok(executed.Result),
                CommandPending pending => Results.Accepted(
                    $"/api/v1/approvals/{pending.ApprovalId}",
                    new { approvalId = pending.ApprovalId, status = "Pending", expiresAt = pending.ExpiresAt }),
                _ => throw new InvalidOperationException("Unknown submission type."),
            };
        });

    /// <summary>Maps the workspace and policy failures to stable HTTP responses without exposing paths or secrets.</summary>
    private static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (PathOutsideWorkspaceException)
        {
            return Results.Problem(
                title: "Path is outside the workspace.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (FileNotFoundException)
        {
            return Results.Problem(title: "File not found.", statusCode: StatusCodes.Status404NotFound);
        }
        catch (DirectoryNotFoundException)
        {
            return Results.Problem(title: "File not found.", statusCode: StatusCodes.Status404NotFound);
        }
        catch (PatchException exception)
        {
            return Results.Problem(
                title: "The edit could not be applied.",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (CommandNotAllowedException exception)
        {
            return Results.Problem(
                title: "The command is not allowed.",
                detail: exception.Decision.Reason,
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["outcome"] = exception.Decision.Outcome.ToString() });
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                title: "Invalid request.",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }
}

/// <summary>Edit request. <c>Search</c> must match exactly once in the file.</summary>
public sealed record EditRequest(string? Path, string? Search, string? Replace);

/// <summary>Command request. <c>Shell</c> is <c>powershell</c> or <c>bash</c>.</summary>
public sealed record CommandRequest(string? Command, string? Shell, int? TimeoutSeconds);
