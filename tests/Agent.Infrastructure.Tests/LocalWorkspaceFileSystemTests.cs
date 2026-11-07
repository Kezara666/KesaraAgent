using Agent.Infrastructure.Workspace;
using Xunit;

namespace Agent.Infrastructure.Tests;

public sealed class LocalWorkspaceFileSystemTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"agent-ws-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // The OS cleans the temp folder later.
        }
    }

    [Fact]
    public async Task FR32ScriptsAreListedSoTheAgentCanSeeThem()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "show-pwd.js"), "console.log(process.cwd());");
        File.WriteAllText(Path.Combine(root, "README.md"), "# readme");
        File.WriteAllBytes(Path.Combine(root, "picture.png"), [0x89, 0x50]);

        var files = await new LocalWorkspaceFileSystem(root).ListTextFilesAsync(CancellationToken.None);

        Assert.Contains("show-pwd.js", files);
        Assert.Contains("README.md", files);
        Assert.DoesNotContain("picture.png", files);
    }
}
