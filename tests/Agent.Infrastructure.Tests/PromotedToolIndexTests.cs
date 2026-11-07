using Agent.Infrastructure.ToolForge;
using Xunit;

namespace Agent.Infrastructure.Tests;

public sealed class PromotedToolIndexTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"agent-tools-{Guid.NewGuid():N}");

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
    public async Task FR52PromotedToolsAreListedFromTheirManifestsAndDamagedOnesAreSkipped()
    {
        var good = Path.Combine(root, "generated", "notes", "write_note");
        var broken = Path.Combine(root, "generated", "notes", "broken");
        Directory.CreateDirectory(good);
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(good, "manifest.json"),
            """{"name":"write_note","domain":"notes","sideEffect":"Write","purpose":"Append a note","promotedAt":"2026-11-07T09:00:00+00:00"}""");
        File.WriteAllText(Path.Combine(broken, "manifest.json"), "{ not json");

        var tools = await new PromotedToolIndex(root).ListAsync(50, CancellationToken.None);

        var tool = Assert.Single(tools);
        Assert.Equal("write_note", tool.Name);
        Assert.Equal("Write", tool.SideEffect);
    }

    [Fact]
    public async Task FR52NoGeneratedFolderMeansNoTools()
    {
        var tools = await new PromotedToolIndex(root).ListAsync(50, CancellationToken.None);

        Assert.Empty(tools);
    }
}
