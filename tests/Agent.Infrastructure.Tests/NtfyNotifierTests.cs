using System.Net;
using System.Text;
using Agent.Infrastructure.Notifications;
using Xunit;

namespace Agent.Infrastructure.Tests;

public sealed class NtfyNotifierTests
{
    [Fact]
    public async Task FR74NotificationPostsTitleAndMessageToTheTopic()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var notifier = new NtfyNotifier(new HttpClient(handler) { BaseAddress = new Uri("https://ntfy.example/") }, "home-agent-topic");

        await notifier.NotifyAsync("Agent entered safe mode", "Promotions are paused.", CancellationToken.None);

        Assert.NotNull(handler.Last);
        Assert.Equal("https://ntfy.example/home-agent-topic", handler.Last.RequestUri!.ToString());
        Assert.Equal("Agent entered safe mode", handler.Last.Headers.GetValues("Title").Single());
        Assert.Equal("Promotions are paused.", handler.Body);
    }

    [Fact]
    public async Task FR74NotificationSendsNoCredentials()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var notifier = new NtfyNotifier(new HttpClient(handler) { BaseAddress = new Uri("https://ntfy.example/") }, "topic");

        await notifier.NotifyAsync("t", "m", CancellationToken.None);

        Assert.Null(handler.Last!.Headers.Authorization);
    }

    [Fact]
    public async Task FR74FailedDeliveryIsReportedNotSwallowed()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError);
        var notifier = new NtfyNotifier(new HttpClient(handler) { BaseAddress = new Uri("https://ntfy.example/") }, "topic");

        await Assert.ThrowsAsync<HttpRequestException>(() => notifier.NotifyAsync("t", "m", CancellationToken.None));
    }

    private sealed class CapturingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(status) { Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain") };
        }
    }
}
