using System.Diagnostics;
using System.Text;
using Agent.Core.Sandbox;

namespace Agent.Sandbox;

/// <summary>
/// Runs each request in a fresh, locked-down Docker container. Docker is driven through its CLI with
/// an argument list, never a shell string, so request content cannot inject options.
/// </summary>
public sealed class DockerSandboxRunner(IReadOnlySet<string> allowedImages, string dockerExecutable = "docker") : ISandboxRunner
{
    private const int MaxOutputCharacters = 64 * 1024;
    private const int MemoryMegabytes = 256;
    private const int ProcessLimit = 128;
    private const string WorkspaceMount = "/work";

    public async Task<SandboxResult> RunAsync(SandboxRequest request, CancellationToken cancellationToken)
    {
        if (!allowedImages.Contains(request.Image))
        {
            throw new InvalidOperationException("The image is not on the sandbox allowlist.");
        }

        if (request.Arguments.Count == 0)
        {
            throw new ArgumentException("At least one argument (the program) is required.", nameof(request));
        }

        var workspace = Path.GetFullPath(request.WorkspacePath);
        Directory.CreateDirectory(workspace);

        var name = $"agent-sbx-{Guid.NewGuid():N}";
        var startInfo = new ProcessStartInfo(dockerExecutable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in BuildRunArguments(request, name, workspace))
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The host environment is not passed. The container gets only what the arguments give it.
        startInfo.Environment.Clear();
        CopyIfPresent(startInfo, "PATH");
        CopyIfPresent(startInfo, "SYSTEMROOT");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = ReadCappedAsync(process.StandardOutput, cancellationToken);
        var stderr = ReadCappedAsync(process.StandardError, cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(request.Timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            await RemoveContainerAsync(name).ConfigureAwait(false);
        }

        return new SandboxResult(
            timedOut ? null : process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false),
            timedOut);
    }

    public static IReadOnlyList<string> BuildRunArguments(SandboxRequest request, string name, string workspace)
    {
        var args = new List<string>
        {
            "run",
            "--rm",
            "--name", name,
            "--network", request.AllowNetwork ? "bridge" : "none",
            "--read-only",
            "--tmpfs", "/tmp:rw,size=64m,mode=1777",
            "--memory", $"{MemoryMegabytes}m",
            "--memory-swap", $"{MemoryMegabytes}m",
            "--cpus", "1",
            "--pids-limit", ProcessLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges",
            "--user", "1000:1000",
            "--workdir", WorkspaceMount,
            "-v", $"{workspace}:{WorkspaceMount}:rw",
            request.Image,
        };

        args.AddRange(request.Arguments);
        return args;
    }

    private async Task RemoveContainerAsync(string name)
    {
        using var kill = new Process
        {
            StartInfo = new ProcessStartInfo(dockerExecutable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        kill.StartInfo.ArgumentList.Add("rm");
        kill.StartInfo.ArgumentList.Add("-f");
        kill.StartInfo.ArgumentList.Add(name);
        kill.Start();
        await kill.WaitForExitAsync().ConfigureAwait(false);
    }

    private static void CopyIfPresent(ProcessStartInfo startInfo, string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (value is not null)
        {
            startInfo.Environment[name] = value;
        }
    }

    private static async Task<string> ReadCappedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            var remaining = MaxOutputCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }
        }

        return builder.ToString();
    }
}
