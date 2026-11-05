using Agent.Core.Shell;

namespace Agent.Core.Workspace;

/// <summary>Read, search, edit, and command operations on the workspace (FR-3.1 to FR-3.4).</summary>
public sealed class WorkspaceService(IWorkspaceFileSystem files, IShellRunner shell)
{
    public const int MaxSearchResults = 200;

    public Task<string> ReadAsync(string relativePath, CancellationToken cancellationToken) =>
        files.ReadTextAsync(relativePath, cancellationToken);

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var effectiveLimit = Math.Clamp(limit, 1, MaxSearchResults);

        var hits = new List<SearchHit>();
        foreach (var path in await files.ListTextFilesAsync(cancellationToken).ConfigureAwait(false))
        {
            var lines = (await files.ReadTextAsync(path, cancellationToken).ConfigureAwait(false))
                .Split('\n');

            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                hits.Add(new SearchHit(path, index + 1, lines[index].TrimEnd('\r')));
                if (hits.Count >= effectiveLimit)
                {
                    return hits;
                }
            }
        }

        return hits;
    }

    /// <summary>Applies one search-and-replace edit and returns the diff of what changed.</summary>
    public async Task<EditResult> EditAsync(string relativePath, string search, string replace, CancellationToken cancellationToken)
    {
        var before = await files.ReadTextAsync(relativePath, cancellationToken).ConfigureAwait(false);
        var after = PatchApplier.ApplySingle(before, search, replace);

        await files.WriteTextAsync(relativePath, after, cancellationToken).ConfigureAwait(false);

        return new EditResult(relativePath, PatchApplier.UnifiedDiff(relativePath, before, after));
    }

    /// <summary>Runs a command only if policy allows it. Denied commands never reach the shell.</summary>
    public async Task<ShellResult> RunCommandAsync(
        string command,
        ShellKind shellKind,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var decision = CommandPolicy.Evaluate(command);
        if (decision.Outcome != CommandOutcome.Allowed)
        {
            throw new CommandNotAllowedException(decision);
        }

        return await shell.RunAsync(new ShellRequest(command, shellKind, timeout), cancellationToken).ConfigureAwait(false);
    }
}

public sealed record SearchHit(string Path, int Line, string Text);

public sealed record EditResult(string Path, string Diff);

/// <summary>Raised when policy refuses or holds a command. Carries the decision for the caller.</summary>
public sealed class CommandNotAllowedException(CommandDecision decision)
    : InvalidOperationException(decision.Reason)
{
    public CommandDecision Decision { get; } = decision;
}
