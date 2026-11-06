using System.Diagnostics;
using Agent.Core.ToolForge;
using Xunit;

namespace Agent.Sandbox.Tests;

/// <summary>
/// Runs generated tests for real in the .NET SDK container: restore with network, then test with none.
/// Skips with a visible reason when Docker is not running.
/// </summary>
public sealed class SandboxToolTestRunnerTests : IDisposable
{
    private const string SdkImage = "mcr.microsoft.com/dotnet/sdk:10.0-alpine";

    private readonly string scratch = Path.Combine(Path.GetTempPath(), $"agent-tool-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(scratch, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Temp cleanup by the OS if a handle is still open or the container user owns a file.
        }
    }

    [SkippableFact]
    public async Task FR52PassingGeneratedTestsReportPassed()
    {
        RequireDocker();
        var runner = new SandboxToolTestRunner(new DockerSandboxRunner(new HashSet<string>(StringComparer.Ordinal) { SdkImage }), SdkImage, scratch);

        var result = await runner.RunAsync(Spec(), new GeneratedTool(Passing, PassingTest), CancellationToken.None);

        Assert.True(result.Passed, result.Summary);
    }

    [SkippableFact]
    public async Task FR52FailingGeneratedTestsReportFailed()
    {
        RequireDocker();
        var runner = new SandboxToolTestRunner(new DockerSandboxRunner(new HashSet<string>(StringComparer.Ordinal) { SdkImage }), SdkImage, scratch);

        var result = await runner.RunAsync(Spec(), new GeneratedTool(Passing, FailingTest), CancellationToken.None);

        Assert.False(result.Passed);
    }

    private static ToolSpec Spec() => new("echo_tool", "text", "Echo the input", SideEffect.ReadOnly, NeedsNetwork: false);

    private const string Passing = "namespace Generated.Tools; public static class EchoTool { public static string Run(string input) => input; }";

    private const string PassingTest = """
        using Xunit;
        using Generated.Tools;
        namespace Generated.Tools.Tests;
        public class EchoToolTests
        {
            [Fact]
            public void ReturnsInput() => Assert.Equal("hi", EchoTool.Run("hi"));
        }
        """;

    private const string FailingTest = """
        using Xunit;
        using Generated.Tools;
        namespace Generated.Tools.Tests;
        public class EchoToolTests
        {
            [Fact]
            public void WrongOnPurpose() => Assert.Equal("nope", EchoTool.Run("hi"));
        }
        """;

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
            Skip.If(process is null, "Docker is not installed. Tool test runs were skipped.");
            process!.WaitForExit(15_000);
            Skip.IfNot(process.HasExited && process.ExitCode == 0, "Docker daemon is not running. Tool test runs were skipped.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Skip.If(true, "Docker is not installed. Tool test runs were skipped.");
        }
    }
}
