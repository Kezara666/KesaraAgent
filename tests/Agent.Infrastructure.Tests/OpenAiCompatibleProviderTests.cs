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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
