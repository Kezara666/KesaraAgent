using System.Diagnostics;
using System.Text;
using Agent.Core.Shell;

namespace Agent.Infrastructure.Shell;

/// <summary>
/// Runs approved commands in the workspace root with a timeout, an output cap, and a minimal
/// environment. Secrets in the host environment are not passed to child processes.
/// </summary>
public sealed class ProcessShellRunner : IShellRunner
{
    public const int MaxOutputCharacters = 64 * 1024;

    private readonly string workingDirectory;

    public ProcessShellRunner(string workingDirectory)
    {
        // A missing working directory makes every process start fail, so create it up front.
        this.workingDirectory = Path.GetFullPath(workingDirectory);
        Directory.CreateDirectory(this.workingDirectory);
    }

    private static readonly string[] InheritedVariables =
    [
        "PATH", "SYSTEMROOT", "TEMP", "TMP", "USERPROFILE", "HOME", "LANG",
    ];

    public async Task<ShellResult> RunAsync(ShellRequest request, CancellationToken cancellationToken)
    {
        var startInfo = BuildStartInfo(request);

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
            KillTree(process);
        }

        return new ShellResult(
            timedOut ? null : process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false),
            timedOut);
    }

    private ProcessStartInfo BuildStartInfo(ShellRequest request)
    {
        var (program, arguments) = request.Shell switch
        {
            ShellKind.PowerShell => (OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh",
                new[] { "-NoProfile", "-NonInteractive", "-Command", request.Command }),
            ShellKind.Bash => ("bash", new[] { "-c", request.Command }),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        var startInfo = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var name in InheritedVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                startInfo.Environment[name] = value;
            }
        }

        return startInfo;
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

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the kill. Nothing left to stop.
        }
    }
}
