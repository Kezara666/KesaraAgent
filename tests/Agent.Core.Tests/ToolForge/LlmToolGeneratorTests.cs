using Agent.Core.Llm;
using Agent.Core.Ports;
using Agent.Core.ToolForge;
using Xunit;

namespace Agent.Core.Tests.ToolForge;

public sealed class LlmToolGeneratorTests
{
    private const string Reply = """
        Here you go.
        ```csharp source
        namespace Generated.Tools; public static class ParseCsv { public static string Run(string input) => input; }
        ```
        ```csharp tests
        namespace Generated.Tools.Tests; public class ParseCsvTests { }
        ```
        """;

    [Fact]
    public async Task FR51GeneratorExtractsBothFencedBlocks()
    {
        var generator = new LlmToolGenerator(new FixedLlm(Reply));

        var tool = await generator.GenerateAsync(new ToolSpec("parse_csv", "text", "Split CSV", SideEffect.ReadOnly, false), null, CancellationToken.None);

        Assert.NotNull(tool);
        Assert.Contains("public static class ParseCsv", tool.Source, StringComparison.Ordinal);
        Assert.Contains("ParseCsvTests", tool.TestSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FR51ReplyWithoutBothBlocksCountsAsNoCode()
    {
        var generator = new LlmToolGenerator(new FixedLlm("Sorry, I cannot help with that."));

        var tool = await generator.GenerateAsync(new ToolSpec("parse_csv", "text", "Split CSV", SideEffect.ReadOnly, false), null, CancellationToken.None);

        Assert.Null(tool);
    }

    [Fact]
    public void FR51PromptCarriesTheContractAndPreviousFeedback()
    {
        var prompt = LlmToolGenerator.BuildPrompt(new ToolSpec("parse_csv", "text", "Split CSV", SideEffect.ReadOnly, false), "CS0103 missing name");

        Assert.Contains("named ParseCsv", prompt, StringComparison.Ordinal);
        Assert.Contains("Run(string input)", prompt, StringComparison.Ordinal);
        Assert.Contains("CS0103 missing name", prompt, StringComparison.Ordinal);
    }

    private sealed class FixedLlm(string text) : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new LlmResponse(text, "fixed", "fixed", 0));
    }
}
