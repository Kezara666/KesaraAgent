using System.Text.RegularExpressions;
using Agent.Core.Shell;
using Agent.Core.ToolForge;
using Agent.Core.Workflows;

namespace Agent.Core.Workspace;

/// <summary>
/// Runs a script from the workspace in the sandbox. Running code is a change, so the agent holds every run for approval
/// before this provider is reached. The run has no network and can write only to the workspace (SRS sandbox rules).
/// </summary>
public sealed partial class WorkspaceRunToolProvider(IShellRunner shell) : IToolProvider
{
    public const string RunName = "workspace_run";

    private const int MaxOutputCharacters = 8000;
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);

    // Only these interpreters, so a run cannot start arbitrary programs. Each maps to the command the sandbox runs.
    private static readonly Dictionary<string, string> Interpreters = new(StringComparer.Ordinal)
    {
        [".js"] = "node",
        [".py"] = "python3",
        [".sh"] = "sh",
    };

    private static readonly IReadOnlyList<ToolDescriptor> Tools =
    [
        new(RunName, SideEffect.Write, ["path"], "Run a script (.js, .py, or .sh) from the workspace in a sandbox with no network. Requires approval."),
    ];

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_./-]{0,200}$")]
    private static partial Regex SafePath();

    public ToolDescriptor? Find(string name) => Tools.FirstOrDefault(tool => tool.Name == name);

    public IReadOnlyList<ToolDescriptor> List() => Tools;

    public async Task<string> InvokeAsync(string tool, IReadOnlyDictionary<string, string> inputs, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (tool != RunName)
        {
            throw new InvalidOperationException("The tool is not available.");
        }

        var command = BuildCommand(inputs.TryGetValue("path", out var path) ? path : string.Empty);
        var result = await shell.RunAsync(new ShellRequest(command, ShellKind.Bash, RunTimeout), cancellationToken).ConfigureAwait(false);

        if (result.TimedOut)
        {
            return "The script did not finish within the time limit and was stopped.";
        }

        var output = $"Exit code {result.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}.\n{result.Stdout}{result.Stderr}";
        return output.Length <= MaxOutputCharacters ? output : output[..MaxOutputCharacters] + "\n[truncated]";
    }

    /// <summary>
    /// The only command this provider can build. The path must be a plain relative path with no spaces or "..", and its
    /// extension must have a known interpreter. Anything else is refused before the sandbox is reached.
    /// </summary>
    public static string BuildCommand(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !SafePath().IsMatch(path) || path.Contains("..", StringComparison.Ordinal) || path.StartsWith('/'))
        {
            throw new ArgumentException("The script path must be a plain relative path inside the workspace.", nameof(path));
        }

        var extension = Path.GetExtension(path);
        if (!Interpreters.TryGetValue(extension, out var interpreter))
        {
            throw new ArgumentException("Only .js, .py, and .sh scripts can be run.", nameof(path));
        }

        return $"{interpreter} {path}";
    }
}
