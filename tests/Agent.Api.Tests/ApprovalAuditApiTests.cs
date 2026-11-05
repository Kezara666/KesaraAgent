using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Agent.Api.Tests;

public sealed class ApprovalAuditApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task FR34HeldCommandReturns202ThenApprovalRunsItOnce()
    {
        using var client = AuthorizedClient();
        var shell = OperatingSystem.IsWindows() ? "powershell" : "bash";

        var submit = await client.PostAsJsonAsync("/api/v1/workspace/commands", new { command = "hostname", shell });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        var id = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("approvalId").GetGuid();

        var pending = await client.GetFromJsonAsync<JsonElement>($"/api/v1/approvals/{id}");
        Assert.Equal("Pending", pending.GetProperty("status").GetString());

        var approve = await client.PostAsync($"/api/v1/approvals/{id}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var decided = await approve.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Executed", decided.GetProperty("status").GetString());

        var second = await client.PostAsync($"/api/v1/approvals/{id}/approve", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task FR34DeniedCommandAppearsInTheAuditLog()
    {
        using var client = AuthorizedClient();
        var denied = await client.PostAsJsonAsync("/api/v1/workspace/commands", new
        {
            command = "Remove-Item -Recurse C:\\",
            shell = "powershell",
        });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/v1/audit?limit=200");

        Assert.Contains(audit.EnumerateArray(), entry =>
            entry.GetProperty("action").GetString() == "command.denied"
            && entry.GetProperty("result").GetString() == "Denied");
    }

    [Fact]
    public async Task FR84EveryApiCallIsAuditedWithCallerAndStatus()
    {
        using var client = AuthorizedClient();
        await client.GetAsync("/api/v1/tasks?limit=1");

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/v1/audit?limit=200");

        Assert.Contains(audit.EnumerateArray(), entry =>
            entry.GetProperty("action").GetString() == "http.GET"
            && entry.GetProperty("target").GetString() == "/api/v1/tasks"
            && entry.GetProperty("result").GetString() == "200"
            && entry.GetProperty("actor").GetString() == "bootstrap-admin");
    }

    [Fact]
    public async Task FR84AuditEndpointRequiresAToken()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient AuthorizedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.TestToken);
        return client;
    }
}
