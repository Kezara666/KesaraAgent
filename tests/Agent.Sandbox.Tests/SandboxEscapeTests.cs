using System.Diagnostics;
using Agent.Core.Sandbox;
using Xunit;

namespace Agent.Sandbox.Tests;

/// <summary>
/// Escape suite (SB-1 to SB-8). Each test runs a real container and checks one isolation property.
/// These tests skip with a visible reason when Docker is not available.
/// </summary>
public sealed class SandboxEscapeTests : IDisposable
{
    private const string Image = "alpine:3.20";
    private const string SentinelVariable = "AGENT_SANDBOX_TEST_SECRET";

    private readonly string workspace = Path.Combine(Path.GetTempPath(), $"agent-sbx-ws-{Guid.NewGuid():N}");
    private readonly DockerSandboxRunner runner = new(new HashSet<string>(StringComparer.Ordinal) { Image });

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SentinelVariable, null);
        try
        {
            Directory.Delete(workspace, recursive: true);
        }
        catch (IOException)
        {
            // Left for OS temp cleanup if a container still holds a handle.
        }
    }

    [SkippableFact]
    public async Task SB1NoNetworkByDefault()
    {
        RequireDocker();

        var result = await Run("wget", "-T", "4", "-q", "-O-", "http://1.1.1.1");

        Assert.False(result.TimedOut);
        Assert.NotEqual(0, result.ExitCode);
    }

    [SkippableFact]
    public async Task SB2RootFilesystemIsReadOnlyOutsideTheWorkspace()
    {
        RequireDocker();

        var result = await Run("sh", "-c", "echo x > /etc/agent-escape");

        Assert.NotEqual(0, result.ExitCode);
    }

    [SkippableFact]
    public async Task SB3WorkspaceIsWritableAndVisibleOnTheHost()
    {
        RequireDocker();

        var result = await Run("sh", "-c", "echo ok > /work/out.txt");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ok", (await File.ReadAllTextAsync(Path.Combine(workspace, "out.txt"))).Trim());
    }

    [SkippableFact]
    public async Task SB4ProcessRunsAsNonRootUser()
    {
        RequireDocker();

        var result = await Run("id", "-u");

        Assert.Equal("1000", result.Stdout.Trim());
    }

    [SkippableFact]
    public async Task SB5HostEnvironmentIsNotInherited()
    {
        RequireDocker();
        Environment.SetEnvironmentVariable(SentinelVariable, "must-not-appear");

        var result = await Run("env");

        Assert.DoesNotContain(SentinelVariable, result.Stdout, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task SB6TimeoutKillsTheContainer()
    {
        RequireDocker();
        var stopwatch = Stopwatch.StartNew();

        var result = await Run(TimeSpan.FromSeconds(3), "sleep", "60");

        Assert.True(result.TimedOut);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Took {stopwatch.Elapsed}.");
    }

    [SkippableFact]
    public async Task SB7ImageOutsideTheAllowlistIsRefused()
    {
        RequireDocker();

        var request = new SandboxRequest("ubuntu:latest", ["id"], workspace, TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(request, CancellationToken.None));
    }

    [SkippableFact]
    public async Task SB8HostFilesystemIsNotVisibleExceptTheWorkspace()
    {
        RequireDocker();

        var result = await Run("ls", "/");

        Assert.DoesNotContain("Users", result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        Assert.DoesNotContain("Program Files", result.Stdout, StringComparison.Ordinal);
    }

    private Task<SandboxResult> Run(params string[] arguments) =>
        Run(TimeSpan.FromSeconds(60), arguments);

    private Task<SandboxResult> Run(TimeSpan timeout, params string[] arguments)
    {
        // The container runs as uid 1000, which is not the host user that created the folder on Linux.
        // The workspace is a throwaway test folder, so it is opened to that user for writing.
        Directory.CreateDirectory(workspace);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(workspace, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        }

        return runner.RunAsync(new SandboxRequest(Image, arguments, workspace, timeout), CancellationToken.None);
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
            Skip.If(process is null, "Docker is not installed. Escape tests were skipped.");
            process!.WaitForExit(15_000);
            Skip.IfNot(process.HasExited && process.ExitCode == 0, "Docker daemon is not running. Escape tests were skipped.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Skip.If(true, "Docker is not installed. Escape tests were skipped.");
        }
    }
}
