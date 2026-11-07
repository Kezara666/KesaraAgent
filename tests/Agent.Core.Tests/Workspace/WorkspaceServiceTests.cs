using Agent.Core.Workspace;
using Xunit;

namespace Agent.Core.Tests.Workspace;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public async Task FR32ListToolReturnsTheWorkspaceFilePaths()
    {
        var files = new InMemoryWorkspaceFiles(new Dictionary<string, string> { ["README.md"] = "a", ["notes/todo.md"] = "b" });
        var tools = new WorkspaceToolProvider(new WorkspaceService(files));

        var output = await tools.InvokeAsync(WorkspaceToolProvider.ListName, new Dictionary<string, string>(), "k", CancellationToken.None);

        Assert.Contains("README.md", output, StringComparison.Ordinal);
        Assert.Contains("notes/todo.md", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FR32EditReturnsDiffAndWritesNewContent()
    {
        var files = new InMemoryWorkspaceFiles(new Dictionary<string, string>
        {
            ["notes.md"] = "alpha\nbeta\n",
        });
        var service = new WorkspaceService(files);

        var result = await service.EditAsync("notes.md", "beta", "BETA", CancellationToken.None);

        Assert.Contains("+BETA", result.Diff, StringComparison.Ordinal);
        Assert.Equal("alpha\nBETA\n", files.Contents["notes.md"]);
    }

    [Fact]
    public async Task FR31SearchFindsMatchesAcrossFilesWithLineNumbers()
    {
        var files = new InMemoryWorkspaceFiles(new Dictionary<string, string>
        {
            ["a.md"] = "one\nTODO: fix\n",
            ["b.md"] = "todo later\n",
        });
        var service = new WorkspaceService(files);

        var hits = await service.SearchAsync("todo", limit: 10, CancellationToken.None);

        Assert.Equal(2, hits.Count);
        Assert.Equal(("a.md", 2), (hits[0].Path, hits[0].Line));
        Assert.Equal(("b.md", 1), (hits[1].Path, hits[1].Line));
    }

    [Fact]
    public async Task FR35CreateWritesANewFileAndNeverOverwritesAnExistingOne()
    {
        var files = new InMemoryWorkspaceFiles(new Dictionary<string, string> { ["notes.md"] = "keep me" });
        var service = new WorkspaceService(files);

        await service.CreateAsync("todo.md", "buy milk", CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => service.CreateAsync("notes.md", "overwrite", CancellationToken.None));

        Assert.Equal("buy milk", files.Contents["todo.md"]);
        Assert.Equal("keep me", files.Contents["notes.md"]);
    }

    private sealed class InMemoryWorkspaceFiles(IDictionary<string, string> initial) : IWorkspaceFileSystem
    {
        public Dictionary<string, string> Contents { get; } = new(initial, StringComparer.Ordinal);

        public Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(Contents.TryGetValue(relativePath, out var text)
                ? text
                : throw new FileNotFoundException(relativePath));

        public Task CreateTextAsync(string relativePath, string content, CancellationToken cancellationToken)
        {
            if (Contents.ContainsKey(relativePath))
            {
                throw new IOException("The file already exists.");
            }

            Contents[relativePath] = content;
            return Task.CompletedTask;
        }

        public Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken)
        {
            Contents[relativePath] = content;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListTextFilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Contents.Keys.OrderBy(path => path, StringComparer.Ordinal).ToList());
    }
}
