namespace Agent.Core.Workspace;

/// <summary>
/// Snapshots the workspace before an edit, so a change can be undone (FR-3.5). Returns the checkpoint id, or null
/// when the workspace cannot be checkpointed (for example, it is not a git repository). The edit still proceeds, and
/// the response says that no checkpoint exists.
/// </summary>
public interface IWorkspaceCheckpoints
{
    Task<string?> CreateAsync(string label, CancellationToken cancellationToken);

    /// <summary>Recent checkpoint ids, newest first.</summary>
    Task<IReadOnlyList<string>> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a checkpoint's files back into the workspace. Files added after the checkpoint are left in place. The
    /// current state is checkpointed first, so the restore itself can be undone.
    /// </summary>
    Task RestoreAsync(string checkpointId, CancellationToken cancellationToken);
}
