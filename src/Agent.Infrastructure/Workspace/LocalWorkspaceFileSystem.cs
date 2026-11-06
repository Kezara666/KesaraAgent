using Agent.Core.Workspace;

namespace Agent.Infrastructure.Workspace;

/// <summary>
/// Local-disk workspace adapter. Confines every path to the root and refuses symbolic links
/// and other reparse points, so a link cannot point the agent outside the workspace.
/// </summary>
public sealed class LocalWorkspaceFileSystem : IWorkspaceFileSystem
{
    private const int MaxFilesListed = 5000;

    private static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".vs",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".slnx", ".sln", ".json", ".md", ".txt",
        ".yml", ".yaml", ".xml", ".ps1", ".sh", ".config", ".editorconfig", ".gitignore",
    };

    private readonly string root;

    public LocalWorkspaceFileSystem(string root)
    {
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }

    public async Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken)
    {
        var fullPath = ResolveSafe(relativePath);
        return await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken)
    {
        var fullPath = ResolveSafe(relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Edits may only change files that already exist.", relativePath);
        }

        await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> ListTextFilesAsync(CancellationToken cancellationToken)
    {
        var results = new List<string>();

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0 && results.Count < MaxFilesListed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            foreach (var subdirectory in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(subdirectory);
                if (ExcludedFolders.Contains(name) || IsLink(subdirectory))
                {
                    continue;
                }

                pending.Push(subdirectory);
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!TextExtensions.Contains(Path.GetExtension(file)) || IsLink(file))
                {
                    continue;
                }

                results.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
                if (results.Count >= MaxFilesListed)
                {
                    break;
                }
            }
        }

        results.Sort(StringComparer.Ordinal);
        return Task.FromResult<IReadOnlyList<string>>(results);
    }

    private string ResolveSafe(string relativePath)
    {
        var fullPath = PathGuard.Resolve(root, relativePath);

        // Check every existing segment between the root and the target for links.
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, fullPath).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current))
            {
                if (IsLink(current))
                {
                    throw new PathOutsideWorkspaceException(relativePath);
                }
            }
        }

        return fullPath;
    }

    private static bool IsLink(string path)
    {
        var attributes = File.GetAttributes(path);
        return attributes.HasFlag(FileAttributes.ReparsePoint);
    }
}
