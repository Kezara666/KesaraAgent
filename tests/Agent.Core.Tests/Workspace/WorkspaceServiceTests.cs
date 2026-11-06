using Agent.Core.Shell;
using Agent.Core.Workspace;
using Xunit;

namespace Agent.Core.Tests.Workspace;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public async Task FR32EditReturnsDiffAndWritesNewContent()
    {
        var files = new InMemoryWorkspaceFiles(new Dictionary<string, string>
        {
            ["notes.md"] = "alpha\nbeta\n",
        });
        var service = new WorkspaceService(files, new RecordingShell());

        var result = await service.EditAsync("notes.md", "beta", "BETA", CancellationToken.None);

        Assert.Contains("+BETA", result.Diff, StringComparison.Ordinal);
        Assert.Equal("alpha\nBETA\n", files.Contents["notes.md"]);
    }

    [Fact]
    public async Task FR34DeniedCommandNeverReachesTheShell()
    {
        var shell = new RecordingShell();
        var service = new WorkspaceService(new InMemoryWorkspaceFiles(new Dictionary<string, string>()), shell);

        var exception = await Assert.ThrowsAsync<CommandNotAllowedException>(() =>
            service.RunCommandAsync("Remove-Item -Recurse C:\\", ShellKind.PowerShell, TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Equal(CommandOutcome.Denied, exception.Decision.Outcome);
        Assert.Equal(0, shell.Runs);
    }

    [Fact]
    public async Task FR31SearchFindsMatchesAcrossFilesWithLineNumbers()
    {
        var files = new InMemoryWorkspaceFiles(new Dictionary<string, string>
        {
            ["a.md"] = "one\nTODO: fix\n",
            ["b.md"] = "todo later\n",
        });
        var service = new WorkspaceService(files, new RecordingShell());

        var hits = await service.SearchAsync("todo", limit: 10, CancellationToken.None);

        Assert.Equal(2, hits.Count);
        Assert.Equal(("a.md", 2), (hits[0].Path, hits[0].Line));
        Assert.Equal(("b.md", 1), (hits[1].Path, hits[1].Line));
    }

    private sealed class InMemoryWorkspaceFiles(IDictionary<string, string> initial) : IWorkspaceFileSystem
    {
        public Dictionary<string, string> Contents { get; } = new(initial, StringComparer.Ordinal);

        public Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(Contents.TryGetValue(relativePath, out var text)
                ? text
                : throw new FileNotFoundException(relativePath));

        public Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken)
        {
            Contents[relativePath] = content;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListTextFilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Contents.Keys.OrderBy(path => path, StringComparer.Ordinal).ToList());
    }

    private sealed class RecordingShell : IShellRunner
    {
        public int Runs { get; private set; }

        public Task<ShellResult> RunAsync(ShellRequest request, CancellationToken cancellationToken)
        {
            Runs++;
            return Task.FromResult(new ShellResult(0, string.Empty, string.Empty, TimedOut: false));
        }
    }
}
