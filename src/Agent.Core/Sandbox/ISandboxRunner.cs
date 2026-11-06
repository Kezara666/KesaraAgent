namespace Agent.Core.Sandbox;

/// <summary>
/// Port for running code in isolation. Adapters must enforce: no network unless requested, a read-only
/// root filesystem, a single writable workspace mount, no inherited host environment, a non-root user,
/// resource limits, and a hard timeout.
/// </summary>
public interface ISandboxRunner
{
    Task<SandboxResult> RunAsync(SandboxRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// One isolated run.
/// </summary>
/// <param name="Image">Container image, pinned by tag. Must be on the configured allowlist.</param>
/// <param name="Arguments">Program and arguments. Run directly, never through a host shell.</param>
/// <param name="WorkspacePath">Host folder mounted read-write at <c>/work</c>. Nothing else is shared.</param>
/// <param name="Timeout">Hard limit. The container is killed when it expires.</param>
/// <param name="AllowNetwork">Off by default. Only generated tools with a declared network permission may enable it.</param>
public sealed record SandboxRequest(
    string Image,
    IReadOnlyList<string> Arguments,
    string WorkspacePath,
    TimeSpan Timeout,
    bool AllowNetwork = false);

/// <summary>Outcome of one isolated run.</summary>
public sealed record SandboxResult(int? ExitCode, string Stdout, string Stderr, bool TimedOut);
