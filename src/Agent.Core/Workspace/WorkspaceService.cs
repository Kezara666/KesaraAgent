namespace Agent.Core.Workspace;

/// <summary>Read, search, and edit operations on the workspace (FR-3.1, FR-3.2). Commands are governed elsewhere.</summary>
public sealed class WorkspaceService(IWorkspaceFileSystem files, IWorkspaceCheckpoints? checkpoints = null)
{
    public const int MaxSearchResults = 200;

    public Task<string> ReadAsync(string relativePath, CancellationToken cancellationToken) =>
        files.ReadTextAsync(relativePath, cancellationToken);

    /// <summary>Text files under the workspace root, as relative paths. Build and version-control folders are excluded.</summary>
    public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken cancellationToken) =>
        files.ListTextFilesAsync(cancellationToken);

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

    /// <summary>Creates a new file with the given content. An existing file is never overwritten.</summary>
    public async Task<string> CreateAsync(string relativePath, string content, CancellationToken cancellationToken)
    {
        await files.CreateTextAsync(relativePath, content, cancellationToken).ConfigureAwait(false);
        return relativePath;
    }

    /// <summary>Applies one search-and-replace edit and returns the diff of what changed.</summary>
    public async Task<EditResult> EditAsync(string relativePath, string search, string replace, CancellationToken cancellationToken)
    {
        var before = await files.ReadTextAsync(relativePath, cancellationToken).ConfigureAwait(false);
        var after = PatchApplier.ApplySingle(before, search, replace);

        // The checkpoint is taken before the write, so the workspace can always be restored to this exact state.
        var checkpointId = checkpoints is null
            ? null
            : await checkpoints.CreateAsync($"before edit to {relativePath}", cancellationToken).ConfigureAwait(false);

        await files.WriteTextAsync(relativePath, after, cancellationToken).ConfigureAwait(false);

        return new EditResult(relativePath, PatchApplier.UnifiedDiff(relativePath, before, after), checkpointId);
    }
}

public sealed record SearchHit(string Path, int Line, string Text);

public sealed record EditResult(string Path, string Diff, string? CheckpointId = null);
