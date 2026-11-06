using Agent.Core.Sandbox;
using Agent.Core.Shell;

namespace Agent.Sandbox;

/// <summary>
/// Runs approved shell commands inside the Docker sandbox instead of on the host. The workspace is the only mount,
/// there is no network, and the root filesystem is read-only. Each shell kind names its own image, because
/// PowerShell and bash do not share one. A kind with no configured image is refused, not run on the host.
/// </summary>
public sealed class SandboxShellRunner(ISandboxRunner sandbox, string workspacePath, string bashImage, string? powerShellImage)
    : IShellRunner
{
    public async Task<ShellResult> RunAsync(ShellRequest request, CancellationToken cancellationToken)
    {
        var (image, arguments) = request.Shell switch
        {
            ShellKind.Bash => (bashImage, new List<string> { "sh", "-c", request.Command }),
            ShellKind.PowerShell when !string.IsNullOrWhiteSpace(powerShellImage)
                => (powerShellImage, new List<string> { "pwsh", "-NoProfile", "-NonInteractive", "-Command", request.Command }),
            ShellKind.PowerShell => throw new InvalidOperationException("PowerShell is not configured for the sandbox."),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        var result = await sandbox.RunAsync(
                new SandboxRequest(image, arguments, workspacePath, request.Timeout),
                cancellationToken)
            .ConfigureAwait(false);

        return new ShellResult(result.ExitCode, result.Stdout, result.Stderr, result.TimedOut);
    }
}
