using Agent.Core.Workspace;
using Xunit;

namespace Agent.Core.Tests.Workspace;

public sealed class PathGuardTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "agent-root");

    [Theory]
    [InlineData("src/Program.cs")]
    [InlineData("docs/notes.md")]
    public void FR31RelativePathInsideRootIsAccepted(string relative)
    {
        var resolved = PathGuard.Resolve(Root, relative);

        Assert.StartsWith(Path.GetFullPath(Root), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("..\\..\\secret.txt")]
    [InlineData("src/../../secret.txt")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share\\file.txt")]
    public void FR31PathThatEscapesRootIsRejected(string relative)
    {
        Assert.Throws<PathOutsideWorkspaceException>(() => PathGuard.Resolve(Root, relative));
    }

    [Fact]
    public void FR31EmptyPathIsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => PathGuard.Resolve(Root, " "));
    }
}
