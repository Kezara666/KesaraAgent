using System.Diagnostics;
using Agent.Core.Shell;
using Xunit;

namespace Agent.Sandbox.Tests;

/// <summary>Approved shell commands run inside the sandbox (SB-9 to SB-11). Skips with a reason if Docker is absent.</summary>
public sealed class SandboxShellRunnerTests : IDisposable
{
    private const string BashImage = "alpine:3.20";

    private readonly string workspace = Path.Combine(Path.GetTempPath(), $"agent-shell-ws-{Guid.NewGuid():N}");
    private readonly SandboxShellRunner shell;

    public SandboxShellRunnerTests()
    {
        shell = new SandboxShellRunner(
            new DockerSandboxRunner(new HashSet<string>(StringComparer.Ordinal) { BashImage }),
            workspace,
            BashImage,
            powerShellImage: null);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workspace, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup by the OS if a handle is still open.
        }
    }

    [SkippableFact]
    public async Task SB9ApprovedCommandRunsInsideTheSandbox()
    {
        RequireDocker();

        var result = await shell.RunAsync(new ShellRequest("echo sandboxed", ShellKind.Bash, TimeSpan.FromSeconds(60)), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("sandboxed", result.Stdout, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task SB10CommandCannotReachTheNetwork()
    {
        RequireDocker();

        var result = await shell.RunAsync(new ShellRequest("wget -T 4 -q -O- http://1.1.1.1", ShellKind.Bash, TimeSpan.FromSeconds(60)), CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
    }

    [SkippableFact]
    public async Task SB11CommandCannotWriteOutsideTheWorkspace()
    {
        RequireDocker();

        var result = await shell.RunAsync(new ShellRequest("echo x > /etc/escape", ShellKind.Bash, TimeSpan.FromSeconds(60)), CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task SB12PowerShellIsRefusedWhenNoImageIsConfigured()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            shell.RunAsync(new ShellRequest("Get-ChildItem", ShellKind.PowerShell, TimeSpan.FromSeconds(5)), CancellationToken.None));
    }

    private static void RequireDocker()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "version --format {{.Server.Version}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            Skip.If(process is null, "Docker is not installed. Sandbox shell tests were skipped.");
            process!.WaitForExit(15_000);
            Skip.IfNot(process.HasExited && process.ExitCode == 0, "Docker daemon is not running. Sandbox shell tests were skipped.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Skip.If(true, "Docker is not installed. Sandbox shell tests were skipped.");
        }
    }
}
