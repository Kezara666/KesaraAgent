namespace Agent.Core.Workspace;

/// <summary>
/// Confines file paths to the workspace root (FR-3.1). Pure path arithmetic: it does not touch the disk.
/// Adapters must also reject symbolic links and reparse points, which only they can see.
/// </summary>
public static class PathGuard
{
    /// <summary>Resolves a relative path inside <paramref name="root"/>, or throws if it escapes.</summary>
    public static string Resolve(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        // Backslashes and colons are separators or drive markers on Windows. Refuse them on every platform,
        // so a path that is safe on Linux cannot be used to escape when the workspace is opened on Windows.
        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith('/') || relativePath.StartsWith('\\')
            || relativePath.Contains('\\', StringComparison.Ordinal) || relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new PathOutsideWorkspaceException(relativePath);
        }

        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));

        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new PathOutsideWorkspaceException(relativePath);
        }

        return fullPath;
    }
}

/// <summary>Raised when a requested path resolves outside the workspace root.</summary>
public sealed class PathOutsideWorkspaceException(string requestedPath)
    : InvalidOperationException("The path is outside the workspace.")
{
    public string RequestedPath { get; } = requestedPath;
}
