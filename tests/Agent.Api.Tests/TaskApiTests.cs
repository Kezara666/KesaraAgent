using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Agent.Api.Tests;

public sealed class TaskApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task FR82OpenApiDocumentAndSwaggerUiAreServed()
    {
        using var client = factory.CreateClient();

        var document = await client.GetAsync("/openapi/v1.json");
        var docs = await client.GetAsync("/docs/index.html");

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Equal(HttpStatusCode.OK, docs.StatusCode);

        var json = await document.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/tasks", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FR81TaskSubmittedWithUiAbsentRunsToDone()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.TestToken);

        var submit = await client.PostAsJsonAsync("/api/v1/tasks", new { prompt = "list my open items" });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);

        var created = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id));

        var status = await WaitForTerminalStatusAsync(client, id!);
        Assert.Equal("Done", status.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(status.GetProperty("result").GetString()));
    }

    [Fact]
    public async Task FR84RequestWithoutTokenIsRejected()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tasks", new { prompt = "hello" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FR84WrongTokenIsRejected()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-the-token");

        var response = await client.GetAsync("/api/v1/tasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FR85RetryWithSameIdempotencyKeyReturnsSameTask()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.TestToken);
        var key = $"key-{Guid.NewGuid():N}";

        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tasks")
        {
            Content = JsonContent.Create(new { prompt = "same request" }),
        };
        first.Headers.Add("Idempotency-Key", key);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tasks")
        {
            Content = JsonContent.Create(new { prompt = "same request" }),
        };
        second.Headers.Add("Idempotency-Key", key);

        var firstResponse = await client.SendAsync(first);
        var secondResponse = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var firstId = (await firstResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var secondId = (await secondResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public async Task FR81EmptyPromptReturnsBadRequest()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.TestToken);

        var response = await client.PostAsJsonAsync("/api/v1/tasks", new { prompt = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FR86HealthEndpointsReportLiveAndReady()
    {
        using var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task FR81UnknownTaskReturnsNotFound()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.TestToken);

        var response = await client.GetAsync($"/api/v1/tasks/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<JsonElement> WaitForTerminalStatusAsync(HttpClient client, string id)
    {
        var deadline = DateTimeOffset.UtcNow.Add(PollTimeout);
        while (true)
        {
            var response = await client.GetAsync($"/api/v1/tasks/{id}");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var status = body.GetProperty("status").GetString();
            if (status is "Done" or "Failed" or "Cancelled")
            {
                return body;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Task {id} did not finish within {PollTimeout}. Last status: {status}.");
            }

            await Task.Delay(100);
        }
    }
}
