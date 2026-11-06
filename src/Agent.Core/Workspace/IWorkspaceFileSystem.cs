namespace Agent.Core.Workspace;

/// <summary>
/// Port for workspace file access. Paths are relative to the workspace root. Adapters must enforce
/// confinement with <see cref="PathGuard"/> and must refuse symbolic links.
/// </summary>
public interface IWorkspaceFileSystem
{
    Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken);

    Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken);

    /// <summary>Lists text files under the root, excluding build and VCS folders. Paths use forward slashes.</summary>
    Task<IReadOnlyList<string>> ListTextFilesAsync(CancellationToken cancellationToken);
}
