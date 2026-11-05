using System.Text.RegularExpressions;

namespace Agent.Core.ToolForge;

/// <summary>
/// Refuses generated code that reaches for capabilities a tool must not have (FR-5.6). This is a fast first gate.
/// The sandbox still enforces isolation at runtime, so this check cannot be the only defence.
/// </summary>
public static class StaticChecks
{
    private static readonly (Regex Pattern, string Reason)[] Rules =
    [
        (new Regex(@"\bSystem\.Diagnostics\b|\bProcess\b\s*\.|\bProcessStartInfo\b", RegexOptions.CultureInvariant), "starts processes"),
        (new Regex(@"\bSystem\.Net\b|\bHttpClient\b|\bSocket\b|\bWebClient\b|\bTcpClient\b", RegexOptions.CultureInvariant), "uses the network"),
        (new Regex(@"\bSystem\.Reflection\.Emit\b|\bAssembly\.Load\b|\bDllImport\b|\bLibraryImport\b", RegexOptions.CultureInvariant), "loads or emits code"),
        (new Regex(@"\bEnvironment\.GetEnvironmentVariable\b|\bEnvironment\.GetEnvironmentVariables\b", RegexOptions.CultureInvariant), "reads environment variables"),
        (new Regex(@"\bFile\.(Delete|Move|Replace)\b|\bDirectory\.Delete\b", RegexOptions.CultureInvariant), "deletes or moves files"),
        (new Regex(@"\bunsafe\b|\bdynamic\b", RegexOptions.CultureInvariant), "uses unsafe or dynamic code"),
    ];

    public static IReadOnlyList<string> Violations(GeneratedTool tool)
    {
        var found = new List<string>();
        foreach (var (pattern, reason) in Rules)
        {
            if (pattern.IsMatch(tool.Source))
            {
                found.Add($"Source {reason}.");
            }
        }

        return found;
    }
}
