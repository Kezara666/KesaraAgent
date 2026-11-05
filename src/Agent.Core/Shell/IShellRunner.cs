namespace Agent.Core.Shell;

/// <summary>Port for running an already-approved command. Adapters apply timeouts and output caps.</summary>
public interface IShellRunner
{
    Task<ShellResult> RunAsync(ShellRequest request, CancellationToken cancellationToken);
}

public enum ShellKind
{
    PowerShell,
    Bash,
}

public sealed record ShellRequest(string Command, ShellKind Shell, TimeSpan Timeout);

public sealed record ShellResult(int? ExitCode, string Stdout, string Stderr, bool TimedOut);
