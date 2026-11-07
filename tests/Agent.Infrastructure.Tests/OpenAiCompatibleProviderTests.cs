using System.Net;
using System.Text;
using Agent.Core.Llm;
using Agent.Infrastructure.Llm;
using Xunit;

namespace Agent.Infrastructure.Tests;

public sealed class OpenAiCompatibleProviderTests
{
    private const string KeyVariable = "AGENT_TEST_PROVIDER_KEY";

    public OpenAiCompatibleProviderTests()
    {
        Environment.SetEnvironmentVariable(KeyVariable, "test-key-not-real");
    }

    [Fact]
    public async Task FR42HttpTooManyRequestsMapsToRateLimited()
    {
        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            CallAsync(HttpStatusCode.TooManyRequests, "{}"));

        Assert.Equal(ProviderFailureKind.RateLimited, exception.Kind);
    }

    [Fact]
    public async Task FR42ServerErrorMapsToUnavailable()
    {
        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            CallAsync(HttpStatusCode.InternalServerError, "{}"));

        Assert.Equal(ProviderFailureKind.Unavailable, exception.Kind);
    }

    [Fact]
    public async Task FR42UnauthorizedMapsToRejectedSoItIsSkippedNotRetried()
    {
        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            CallAsync(HttpStatusCode.Unauthorized, "{}"));

        Assert.Equal(ProviderFailureKind.Rejected, exception.Kind);
    }

    [Fact]
    public async Task FR41SuccessfulReplyReturnsTextAndReportedTokenCounts()
    {
        const string body = """
            {"choices":[{"message":{"content":"hello back"}}],
             "usage":{"prompt_tokens":12,"completion_tokens":7}}
            """;

        var reply = await CallAsync(HttpStatusCode.OK, body);

        Assert.Equal("hello back", reply.Text);
        Assert.Equal(12, reply.InputTokens);
        Assert.Equal(7, reply.OutputTokens);
    }

    [Fact]
    public async Task FR41MissingKeyIsRejectedBeforeAnyNetworkCall()
    {
        Environment.SetEnvironmentVariable(KeyVariable, null);
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var provider = new OpenAiCompatibleProvider("p", new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") }, KeyVariable);

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.CompleteAsync("model", "hi", CancellationToken.None));

        Assert.Equal(ProviderFailureKind.Rejected, exception.Kind);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task FR41RequestUsesTheLowercaseFieldNamesOpenAiEndpointsExpect()
    {
        const string reply = """{"choices":[{"message":{"content":"ok"}}]}""";
        var handler = new StubHandler(HttpStatusCode.OK, reply);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") };
        var provider = new OpenAiCompatibleProvider("p", http, KeyVariable);

        await provider.CompleteAsync("the-model", "hi", CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"model\":\"the-model\"", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FR41ToolsAreSentInTheStandardFormat()
    {
        const string reply = """{"choices":[{"message":{"content":"ok"}}]}""";
        var handler = new StubHandler(HttpStatusCode.OK, reply);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") };
        var provider = new OpenAiCompatibleProvider("p", http, KeyVariable);
        var tools = new[] { new ModelTool("workspace_read", "Read a file.", ["path"]) };

        await provider.ChatAsync("the-model", [new ChatTurn("user", "read it")], tools, CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"tools\":[{\"type\":\"function\",\"function\":{\"name\":\"workspace_read\"", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"path\"]", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("\"tool_choice\":\"auto\"", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FR41ToolCallsInTheReplyAreReturnedWithTheirArguments()
    {
        const string body = """
            {"choices":[{"message":{"content":null,"tool_calls":[
              {"id":"call-1","type":"function","function":{"name":"workspace_read","arguments":"{\"path\":\"notes.md\",\"line\":3}"}}
            ]}}],"usage":{"prompt_tokens":5,"completion_tokens":2}}
            """;
        var handler = new StubHandler(HttpStatusCode.OK, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") };
        var provider = new OpenAiCompatibleProvider("p", http, KeyVariable);

        var answer = await provider.ChatAsync("the-model", [new ChatTurn("user", "read it")], [], CancellationToken.None);

        var call = Assert.Single(answer.ToolCalls);
        Assert.Equal("call-1", call.Id);
        Assert.Equal("workspace_read", call.Name);
        Assert.Equal("notes.md", call.Arguments["path"]);
        Assert.Equal("3", call.Arguments["line"]);
    }

    private static async Task<ProviderReply> CallAsync(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/") };
        var provider = new OpenAiCompatibleProvider("p", http, KeyVariable);

        return await provider.CompleteAsync("model", "hi", CancellationToken.None);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls++;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }

    }
}
