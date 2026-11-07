using Agent.Core.Shell;
using Agent.Core.Workspace;
using Xunit;

namespace Agent.Core.Tests.Workspace;

public sealed class WorkspaceRunToolProviderTests
{
    [Theory]
    [InlineData("show-pwd.js", "node show-pwd.js")]
    [InlineData("tools/check.py", "python3 tools/check.py")]
    [InlineData("build.sh", "sh build.sh")]
    public void FR32ScriptsWithKnownInterpretersBuildTheirCommand(string path, string expected)
    {
        Assert.Equal(expected, WorkspaceRunToolProvider.BuildCommand(path));
    }

    [Theory]
    [InlineData("../secret.js")]
    [InlineData("/etc/passwd.sh")]
    [InlineData("my script.js")]
    [InlineData("program.exe")]
    [InlineData("run.js; rm -rf /")]
    [InlineData("")]
    public void FR32UnsafeOrUnknownScriptsAreRefusedBeforeTheSandbox(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => WorkspaceRunToolProvider.BuildCommand(path));
    }

    [Fact]
    public async Task FR32RunSendsTheBuiltCommandToTheSandboxShellAndReturnsItsOutput()
    {
        var shell = new RecordingShell();
        var tools = new WorkspaceRunToolProvider(shell);

        var output = await tools.InvokeAsync(
            WorkspaceRunToolProvider.RunName,
            new Dictionary<string, string> { ["path"] = "show-pwd.js" },
            "k",
            CancellationToken.None);

        Assert.Equal("node show-pwd.js", shell.LastCommand);
        Assert.Equal(ShellKind.Bash, shell.LastShell);
        Assert.StartsWith("Exit code 0.", output, StringComparison.Ordinal);
        Assert.Contains("/work", output, StringComparison.Ordinal);
    }

    private sealed class RecordingShell : IShellRunner
    {
        public string? LastCommand { get; private set; }

        public ShellKind? LastShell { get; private set; }

        public Task<ShellResult> RunAsync(ShellRequest request, CancellationToken cancellationToken)
        {
            LastCommand = request.Command;
            LastShell = request.Shell;
            return Task.FromResult(new ShellResult(ExitCode: 0, Stdout: "/work\n", Stderr: string.Empty, TimedOut: false));
        }
    }
}
