namespace Agent.Core.Workspace;

/// <summary>
/// Snapshots the workspace before an edit, so a change can be undone (FR-3.5). Returns the checkpoint id, or null
/// when the workspace cannot be checkpointed (for example, it is not a git repository). The edit still proceeds, and
/// the response says that no checkpoint exists.
/// </summary>
public interface IWorkspaceCheckpoints
{
    Task<string?> CreateAsync(string label, CancellationToken cancellationToken);
}
