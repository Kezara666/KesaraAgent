using System.Diagnostics;
using Agent.Infrastructure.Workspace;
using Xunit;

namespace Agent.Infrastructure.Tests;

public sealed class GitWorkspaceCheckpointsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"agent-ckpt-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        // Git marks object files read-only on Windows, so the attribute is cleared before the folder is deleted.
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // The OS cleans the temp folder later if a handle is still open.
        }
    }

    [Fact]
    public async Task FR35CheckpointSnapshotsTheWorkingTreeWithoutChangingTheUsersStaging()
    {
        Directory.CreateDirectory(root);
        Git("init", "-q");
        File.WriteAllText(Path.Combine(root, "notes.md"), "before the edit");
        var staged = Git("status", "--porcelain");

        var checkpoints = new GitWorkspaceCheckpoints(root);
        var id = await checkpoints.CreateAsync("before edit to notes.md", CancellationToken.None);

        Assert.NotNull(id);
        Assert.Equal("before the edit", Git("show", $"{id}:notes.md"));
        Assert.Equal(staged, Git("status", "--porcelain"));
        Assert.Contains(id, Git("for-each-ref", "refs/agent/checkpoints"));
    }

    [Fact]
    public async Task FR35WorkspaceThatIsNotARepositoryHasNoCheckpoint()
    {
        Directory.CreateDirectory(root);

        var id = await new GitWorkspaceCheckpoints(root).CreateAsync("before edit", CancellationToken.None);

        Assert.Null(id);
    }

    private string Git(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return output.Trim();
    }
}
