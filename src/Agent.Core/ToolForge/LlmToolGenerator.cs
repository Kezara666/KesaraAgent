using System.Text.RegularExpressions;
using Agent.Core.Llm;
using Agent.Core.Ports;

namespace Agent.Core.ToolForge;

/// <summary>
/// Asks the model for one tool and its tests, in two fenced blocks. Anything else counts as a failed attempt.
/// The contract is fixed so the static checks, compiler, and sandbox all see the same shape.
/// </summary>
public sealed class LlmToolGenerator(ILlmClient llm) : IToolGenerator
{
    private static readonly Regex SourceBlock = new(@"```csharp\s+source\s*\n(?<code>.*?)```", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex TestBlock = new(@"```csharp\s+tests\s*\n(?<code>.*?)```", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public async Task<GeneratedTool?> GenerateAsync(ToolSpec spec, string? previousFeedback, CancellationToken cancellationToken)
    {
        var response = await llm.CompleteAsync(new LlmRequest(BuildPrompt(spec, previousFeedback)), cancellationToken)
            .ConfigureAwait(false);

        var source = SourceBlock.Match(response.Text);
        var tests = TestBlock.Match(response.Text);
        if (!source.Success || !tests.Success)
        {
            return null;
        }

        return new GeneratedTool(source.Groups["code"].Value.Trim(), tests.Groups["code"].Value.Trim());
    }

    public static string BuildPrompt(ToolSpec spec, string? previousFeedback)
    {
        var pascal = ToPascalCase(spec.Name);
        var feedback = string.IsNullOrWhiteSpace(previousFeedback)
            ? string.Empty
            : $"\nThe previous attempt failed: {previousFeedback}\nFix that problem.";

        return $"""
            Write one C# tool. Purpose: {spec.Purpose}

            Rules:
            - Namespace Generated.Tools. One public static class named {pascal}.
            - One public static method: public static string Run(string input).
            - Use only the BCL. No processes, no network, no environment variables, no reflection emit, no unsafe, no dynamic, no file deletion.
            - Return plain text.
            - Also write xUnit tests for it, in namespace Generated.Tools.Tests.

            Reply with exactly two fenced blocks and nothing else:
            ```csharp source
            (the tool)
            ```
            ```csharp tests
            (the tests)
            ```
            {feedback}
            """;
    }

    private static string ToPascalCase(string name) =>
        string.Concat(name.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
