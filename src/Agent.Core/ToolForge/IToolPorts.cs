namespace Agent.Core.ToolForge;

/// <summary>Writes generated code. Implemented with the LLM gateway; the prompt template lives in Core.</summary>
public interface IToolGenerator
{
    Task<GeneratedTool?> GenerateAsync(ToolSpec spec, string? previousFeedback, CancellationToken cancellationToken);
}

/// <summary>Compiles generated code without running it. Adapters use Roslyn.</summary>
public interface IToolCompiler
{
    CompileResult Compile(GeneratedTool tool);
}

public sealed record CompileResult(bool Succeeded, IReadOnlyList<string> Errors);

/// <summary>Runs the generated tests in isolation. Adapters use the sandbox runner.</summary>
public interface IToolTestRunner
{
    Task<TestRunResult> RunAsync(ToolSpec spec, GeneratedTool tool, CancellationToken cancellationToken);
}

public sealed record TestRunResult(bool Passed, string Summary);

/// <summary>Staging and promotion storage. Promotion writes the manifest and returns the path it used.</summary>
public interface IToolRepository
{
    Task StageAsync(ToolSpec spec, GeneratedTool tool, CancellationToken cancellationToken);

    Task<string> PromoteAsync(ToolSpec spec, string reason, CancellationToken cancellationToken);
}

/// <summary>Records a promotion in version control so every promoted tool has history (US-4.2).</summary>
public interface IToolHistory
{
    Task<string> CommitAsync(string relativePath, string message, CancellationToken cancellationToken);
}
