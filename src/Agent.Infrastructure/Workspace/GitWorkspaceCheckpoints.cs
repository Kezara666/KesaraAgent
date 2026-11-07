using System.Diagnostics;
using System.Text.RegularExpressions;
using Agent.Core.Workspace;

namespace Agent.Infrastructure.Workspace;

/// <summary>
/// Git-backed checkpoints (FR-3.5). The working tree is written into a commit under <c>refs/agent/checkpoints/</c>
/// using a private index file. The user's index, branch, and working tree are not changed. The commit id is returned,
/// and the ref keeps the snapshot from being garbage-collected.
/// </summary>
public sealed partial class GitWorkspaceCheckpoints(string repositoryRoot) : IWorkspaceCheckpoints
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    private const string Author = "kesara-agent";
    private const string AuthorEmail = "agent@localhost";
    private const string CheckpointRefs = "refs/agent/checkpoints/";

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitIdPattern();

    public async Task<IReadOnlyList<string>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        if (!IsRepository())
        {
            return [];
        }

        var output = await RunGitAsync(["for-each-ref", "--sort=-creatordate", "--format=%(objectname)", CheckpointRefs], null, cancellationToken)
            .ConfigureAwait(false);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(limit).ToList();
    }

    public async Task RestoreAsync(string checkpointId, CancellationToken cancellationToken)
    {
        if (!CommitIdPattern().IsMatch(checkpointId))
        {
            throw new ArgumentException("The checkpoint id is not valid.", nameof(checkpointId));
        }

        var known = await ListAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);
        if (!known.Contains(checkpointId, StringComparer.Ordinal))
        {
            throw new ArgumentException("The checkpoint does not exist.", nameof(checkpointId));
        }

        // Keep the current state first, so the restore can be undone.
        await CreateAsync("before restore to " + checkpointId, cancellationToken).ConfigureAwait(false);

        // A private index writes the snapshot into the working tree. The user's index and branch are not touched.
        var indexFile = Path.Combine(Path.GetTempPath(), $"agent-restore-{Guid.NewGuid():N}");
        var privateIndex = new Dictionary<string, string?>(StringComparer.Ordinal) { ["GIT_INDEX_FILE"] = indexFile };
        try
        {
            await RunGitAsync(["read-tree", checkpointId], privateIndex, cancellationToken).ConfigureAwait(false);
            await RunGitAsync(["checkout-index", "-a", "-f"], privateIndex, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(indexFile);
        }
    }

    private bool IsRepository() =>
        Directory.Exists(Path.Combine(repositoryRoot, ".git")) || File.Exists(Path.Combine(repositoryRoot, ".git"));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The temporary index is outside the workspace, so the OS cleans it up if it is still locked.
        }
    }

    public async Task<string?> CreateAsync(string label, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(repositoryRoot, ".git")) && !File.Exists(Path.Combine(repositoryRoot, ".git")))
        {
            return null;
        }

        var indexFile = Path.Combine(Path.GetTempPath(), $"agent-index-{Guid.NewGuid():N}");
        var privateIndex = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_INDEX_FILE"] = indexFile,
            ["GIT_AUTHOR_NAME"] = Author,
            ["GIT_AUTHOR_EMAIL"] = AuthorEmail,
            ["GIT_COMMITTER_NAME"] = Author,
            ["GIT_COMMITTER_EMAIL"] = AuthorEmail,
        };

        try
        {
            await RunGitAsync(["add", "-A"], privateIndex, cancellationToken).ConfigureAwait(false);
            var tree = (await RunGitAsync(["write-tree"], privateIndex, cancellationToken).ConfigureAwait(false)).Trim();

            var commitArguments = new List<string> { "commit-tree", tree, "-m", label };
            var head = await TryRunGitAsync(["rev-parse", "--verify", "-q", "HEAD"], null, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(head))
            {
                commitArguments.Add("-p");
                commitArguments.Add(head.Trim());
            }

            var commit = (await RunGitAsync(commitArguments, privateIndex, cancellationToken).ConfigureAwait(false)).Trim();
            await RunGitAsync(["update-ref", $"refs/agent/checkpoints/{Guid.CreateVersion7():N}", commit], null, cancellationToken)
                .ConfigureAwait(false);
            return commit;
        }
        finally
        {
            try
            {
                File.Delete(indexFile);
            }
            catch (IOException)
            {
                // The temporary index is outside the workspace, so the OS cleans it up if it is still locked.
            }
        }
    }

    private async Task<string?> TryRunGitAsync(string[] arguments, IReadOnlyDictionary<string, string?>? environment, CancellationToken cancellationToken)
    {
        try
        {
            return await RunGitAsync(arguments, environment, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<string> RunGitAsync(IEnumerable<string> arguments, IReadOnlyDictionary<string, string?>? environment, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                process.StartInfo.Environment[key] = value;
            }
        }

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(CommandTimeout);
        try
        {
            await process.WaitForExitAsync(bound.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("A git checkpoint command timed out.");
        }

        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            // Git's message names paths and refs, never secrets, so it is safe to include.
            throw new InvalidOperationException($"git exited with {process.ExitCode}: {error.Trim()}");
        }

        return output;
    }
}
