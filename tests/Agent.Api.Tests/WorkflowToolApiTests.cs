using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Agent.Api.Tests;

public sealed class WorkflowToolApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task FR91PlanWithAnUnregisteredToolIsRefused()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/v1/workflows", new
        {
            steps = new[] { new { id = "s1", tool = "not_registered", inputs = new Dictionary<string, string>() } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FR91PlanWithoutStepsIsRefused()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/v1/workflows", new { steps = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FR91UnknownRunIsReportedAsNotFound()
    {
        using var client = AuthorizedClient();

        var response = await client.GetAsync("/api/v1/workflows/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FR91CancellingAnUnknownRunIsAConflict()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsync("/api/v1/workflows/does-not-exist/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task FR51ForgingWithAnUnknownSideEffectIsRefused()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/v1/tools/forge", new
        {
            name = "write_note",
            domain = "notes",
            purpose = "Append a note",
            sideEffect = "Delete everything",
            needsNetwork = false,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FR51ToolListIsReturnedAndEmptyWhenNoServerIsConfigured()
    {
        using var client = AuthorizedClient();

        var response = await client.GetAsync("/api/v1/tools");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Empty(body.EnumerateArray());
    }

    [Fact]
    public async Task FR54DecidingAnUnknownApprovalIsReportedAsNotFound()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsync($"/api/v1/approvals/{Guid.CreateVersion7()}/approve", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approval not found.", body.GetProperty("title").GetString());
    }

    private HttpClient AuthorizedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.TestToken);
        return client;
    }
}
