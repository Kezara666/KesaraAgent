using Agent.Core.Sandbox;
using Agent.Core.ToolForge;

namespace Agent.Sandbox;

/// <summary>
/// Runs a generated tool's tests in two sandboxed steps. Step 1 restores the fixed package list with network
/// access, because NuGet needs it. Step 2 runs the tests with no network, using only the restored packages.
/// The project file is written by this runner, so generated code cannot change its dependencies.
/// </summary>
public sealed class SandboxToolTestRunner(ISandboxRunner sandbox, string image, string scratchRoot) : IToolTestRunner
{
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(3);

    private const string ProjectFile = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <IsTestProject>true</IsTestProject>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            <PackageReference Include="xunit" Version="2.9.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
          </ItemGroup>
        </Project>
        """;

    public async Task<TestRunResult> RunAsync(ToolSpec spec, GeneratedTool tool, CancellationToken cancellationToken)
    {
        spec.Validate();
        var folder = Path.Combine(Path.GetFullPath(scratchRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        if (OperatingSystem.IsLinux())
        {
            // The container user (uid 1000) must write restore output here. The folder is deleted after the run.
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        }

        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "Tool.csproj"), ProjectFile, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(folder, "Tool.cs"), tool.Source, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(folder, "Tool.Tests.cs"), tool.TestSource, cancellationToken)
                .ConfigureAwait(false);

            var restore = await sandbox.RunAsync(
                    new SandboxRequest(image, ["dotnet", "restore", "--nologo"], folder, RestoreTimeout, AllowNetwork: true, Variables: Variables()),
                    cancellationToken)
                .ConfigureAwait(false);

            if (restore.TimedOut || restore.ExitCode != 0)
            {
                return new TestRunResult(false, "Restore failed inside the sandbox.");
            }

            var test = await sandbox.RunAsync(
                    new SandboxRequest(image, ["dotnet", "test", "--no-restore", "--nologo"], folder, TestTimeout, AllowNetwork: false, Variables: Variables()),
                    cancellationToken)
                .ConfigureAwait(false);

            if (test.TimedOut)
            {
                return new TestRunResult(false, "Tests timed out.");
            }

            var passed = test.ExitCode == 0;
            return new TestRunResult(passed, Tail(test.Stdout));
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    private static Dictionary<string, string> Variables() => new(StringComparer.Ordinal)
    {
        ["HOME"] = "/work",
        ["DOTNET_CLI_HOME"] = "/work/.dotnet",
        ["NUGET_PACKAGES"] = "/work/.nuget/packages",
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
    };

    private static string Tail(string text)
    {
        const int Limit = 400;
        var trimmed = text.Trim();
        return trimmed.Length <= Limit ? trimmed : trimmed[^Limit..];
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Files the container user created may not be deletable by the host user. The OS removes the scratch folder later.
        }
    }
}
