using System.Diagnostics;
using Agent.Core.ToolForge;
using Agent.Infrastructure.ToolForge;
using Xunit;

namespace Agent.Infrastructure.Tests;

public sealed class ToolForgeAdapterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"agent-forge-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        // git marks object files read-only, and Windows refuses to delete read-only files.
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup by the OS if a handle is still open.
        }
    }

    [Fact]
    public void FR52RoslynAcceptsWellFormedTool()
    {
        var compiler = new RoslynToolCompiler();

        var result = compiler.Compile(new GeneratedTool(
            "namespace Generated.Tools; public static class Echo { public static string Run(string input) => input; }",
            string.Empty));

        Assert.True(result.Succeeded, string.Join(';', result.Errors));
    }

    [Fact]
    public void FR52RoslynRejectsBrokenCode()
    {
        var compiler = new RoslynToolCompiler();

        var result = compiler.Compile(new GeneratedTool("namespace Generated.Tools; public static class Broken {", string.Empty));

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task FR54StagingThenPromotionWritesManifestAndRemovesStaging()
    {
        var repository = new FileToolRepository(root);
        var spec = new ToolSpec("parse_csv", "text", "Split CSV", SideEffect.ReadOnly, NeedsNetwork: false);
        await repository.StageAsync(spec, new GeneratedTool("class A {}", "class B {}"), CancellationToken.None);

        var path = await repository.PromoteAsync(spec, "passed checks", CancellationToken.None);

        Assert.Equal("generated/text/parse_csv", path.Replace('\\', '/'));
        var target = Path.Combine(root, "generated", "text", "parse_csv");
        Assert.True(File.Exists(Path.Combine(target, "parse_csv.cs")));
        Assert.True(File.Exists(Path.Combine(target, "manifest.json")));
        Assert.False(Directory.Exists(Path.Combine(root, "staging", "parse_csv")));
    }

    [Fact]
    public async Task FR52PromotionOfUnstagedToolIsRefused()
    {
        var repository = new FileToolRepository(root);
        var spec = new ToolSpec("never_staged", "text", "x", SideEffect.ReadOnly, NeedsNetwork: false);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            repository.PromoteAsync(spec, "reason", CancellationToken.None));
    }

    [Fact]
    public async Task FR54PromotionCreatesAGitCommitForOnlyThePromotedPath()
    {
        Directory.CreateDirectory(root);
        RunGit(root, "init", "-q");
        File.WriteAllText(Path.Combine(root, "unrelated.txt"), "not part of the promotion");

        var repository = new FileToolRepository(root);
        var spec = new ToolSpec("parse_csv", "text", "Split CSV", SideEffect.ReadOnly, NeedsNetwork: false);
        await repository.StageAsync(spec, new GeneratedTool("class A {}", "class B {}"), CancellationToken.None);
        var path = await repository.PromoteAsync(spec, "passed checks", CancellationToken.None);

        var commit = await new GitToolHistory(root).CommitAsync(path, "feat(tools): FR-5.4 promote parse_csv", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(commit));
        var committed = RunGit(root, "show", "--name-only", "--format=", "HEAD");
        Assert.Contains("generated/text/parse_csv/parse_csv.cs", committed, StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated.txt", committed, StringComparison.Ordinal);
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // git only reads -c options before the subcommand.
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("user.name=Test");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("user.email=test@localhost");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("git did not start.");
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
