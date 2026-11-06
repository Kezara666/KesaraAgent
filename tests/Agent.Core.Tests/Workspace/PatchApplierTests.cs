using Agent.Core.Workspace;
using Xunit;

namespace Agent.Core.Tests.Workspace;

public sealed class PatchApplierTests
{
    [Fact]
    public void FR32UniqueMatchIsReplaced()
    {
        const string original = "alpha\nbeta\ngamma\n";

        var result = PatchApplier.ApplySingle(original, "beta", "BETA");

        Assert.Equal("alpha\nBETA\ngamma\n", result);
    }

    [Fact]
    public void FR32SearchTextThatIsMissingIsRefused()
    {
        var exception = Assert.Throws<PatchException>(() =>
            PatchApplier.ApplySingle("alpha\n", "omega", "x"));

        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FR32AmbiguousSearchTextIsRefusedSoNothingChangesSilently()
    {
        var exception = Assert.Throws<PatchException>(() =>
            PatchApplier.ApplySingle("x\nx\n", "x", "y"));

        Assert.Contains("more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FR32UnifiedDiffShowsRemovedAndAddedLines()
    {
        var diff = PatchApplier.UnifiedDiff("notes.md", "alpha\nbeta\n", "alpha\nBETA\n");

        Assert.StartsWith("--- a/notes.md", diff, StringComparison.Ordinal);
        Assert.Contains("\n-beta", diff, StringComparison.Ordinal);
        Assert.Contains("\n+BETA", diff, StringComparison.Ordinal);
    }
}
