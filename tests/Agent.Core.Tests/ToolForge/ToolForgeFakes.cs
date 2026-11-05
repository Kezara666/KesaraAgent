using Agent.Core.ToolForge;

namespace Agent.Core.Tests.ToolForge;

internal sealed class QueueGenerator(params GeneratedTool?[] responses) : IToolGenerator
{
    public int Calls { get; private set; }

    public string? LastFeedback { get; private set; }

    public Task<GeneratedTool?> GenerateAsync(ToolSpec spec, string? previousFeedback, CancellationToken cancellationToken)
    {
        LastFeedback = previousFeedback;
        var response = Calls < responses.Length ? responses[Calls] : responses[^1];
        Calls++;
        return Task.FromResult(response);
    }
}

internal sealed class FakeCompiler(bool succeeds) : IToolCompiler
{
    public int Calls { get; private set; }

    public CompileResult Compile(GeneratedTool tool)
    {
        Calls++;
        return succeeds ? new CompileResult(true, []) : new CompileResult(false, ["CS0001: broken"]);
    }
}

internal sealed class FakeTestRunner(bool passes) : IToolTestRunner
{
    public int Calls { get; private set; }

    public Task<TestRunResult> RunAsync(ToolSpec spec, GeneratedTool tool, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(passes ? new TestRunResult(true, "1 passed") : new TestRunResult(false, "1 failed"));
    }
}

internal sealed class InMemoryToolRepository : IToolRepository
{
    public List<string> Staged { get; } = [];

    public List<string> Promoted { get; } = [];

    public Task StageAsync(ToolSpec spec, GeneratedTool tool, CancellationToken cancellationToken)
    {
        Staged.Add(spec.Name);
        return Task.CompletedTask;
    }

    public Task<string> PromoteAsync(ToolSpec spec, string reason, CancellationToken cancellationToken)
    {
        Promoted.Add(spec.Name);
        return Task.FromResult($"tools/generated/{spec.Domain}/{spec.Name}");
    }
}

internal sealed class InMemoryToolHistory : IToolHistory
{
    public List<string> Commits { get; } = [];

    public Task<string> CommitAsync(string relativePath, string message, CancellationToken cancellationToken)
    {
        Commits.Add(message);
        return Task.FromResult("abc1234");
    }
}
