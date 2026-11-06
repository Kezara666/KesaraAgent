using System.Diagnostics;
using Agent.Core.ToolForge;

namespace Agent.Infrastructure.ToolForge;

/// <summary>
/// Commits promoted tools with the git CLI. Arguments are passed as a list, never through a shell,
/// and only the promoted path is staged, so unrelated changes are never committed.
/// </summary>
public sealed class GitToolHistory(string repositoryRoot) : IToolHistory
{
    private readonly string repo = Path.GetFullPath(repositoryRoot);

    public async Task<string> CommitAsync(string relativePath, string message, CancellationToken cancellationToken)
    {
        await RunGitAsync(cancellationToken, "add", "--", relativePath).ConfigureAwait(false);
        await RunGitAsync(cancellationToken, "-c", "user.name=Agent Tool Forge", "-c", "user.email=agent@localhost",
            "commit", "-m", message, "--", relativePath).ConfigureAwait(false);
        return (await RunGitAsync(cancellationToken, "rev-parse", "--short", "HEAD").ConfigureAwait(false)).Trim();
    }

    private async Task<string> RunGitAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("git could not be started.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        return output;
    }
}
