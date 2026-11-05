namespace Agent.Core.Shell;

/// <summary>
/// Decides whether a shell command may run (FR-3.4). Fails closed: anything not explicitly
/// allowed is refused or held for approval. The policy runs before any process starts.
/// </summary>
public static class CommandPolicy
{
    /// <summary>Phrases that are refused anywhere in the command, case-insensitively.</summary>
    private static readonly string[] DeniedPhrases =
    [
        "format ", "rm -rf", "remove-item", "rmdir /s", "rd /s", "del /", "diskpart", "mkfs", "dd if=",
        "reg delete", "shutdown", "restart-computer", "stop-computer",
        "invoke-webrequest", "invoke-restmethod", "curl ", "wget ",
        "net user", "net localgroup", "runas", "sudo ", "chmod 777",
        "invoke-expression", "iex ", "start-process",
        "git push", "git remote", "git reset --hard", "git clean -f", "git checkout --",
    ];

    /// <summary>Shell control operators. Chaining, piping, and substitution are refused outright.</summary>
    private static readonly string[] ControlOperators = [";", "&&", "||", "|", "`", "$(", ">", "<", "\n", "\r"];

    /// <summary>Programs that may run without approval.</summary>
    private static readonly HashSet<string> AllowedPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet", "git", "ls", "dir", "pwd", "echo", "cat", "type", "get-childitem", "get-content", "get-location",
    };

    public static CommandDecision Evaluate(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return CommandDecision.Deny("The command is empty.");
        }

        var trimmed = command.Trim();

        if (ControlOperators.Any(trimmed.Contains))
        {
            return CommandDecision.Deny("Chaining, piping, redirection, and substitution are not allowed.");
        }

        if (DeniedPhrases.Any(phrase => trimmed.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return CommandDecision.Deny("The command matches a denied pattern.");
        }

        var program = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];

        if (program.Contains('/') || program.Contains('\\') || program.Contains(':'))
        {
            return CommandDecision.Deny("Programs must be named, not given by path.");
        }

        return AllowedPrograms.Contains(program)
            ? CommandDecision.Allow()
            : CommandDecision.RequireApproval($"'{program}' is not on the allowlist and needs approval.");
    }
}

public enum CommandOutcome
{
    Allowed,
    Denied,
    RequiresApproval,
}

public sealed record CommandDecision(CommandOutcome Outcome, string Reason)
{
    public static CommandDecision Allow() => new(CommandOutcome.Allowed, "Allowed by policy.");

    public static CommandDecision Deny(string reason) => new(CommandOutcome.Denied, reason);

    public static CommandDecision RequireApproval(string reason) => new(CommandOutcome.RequiresApproval, reason);
}
