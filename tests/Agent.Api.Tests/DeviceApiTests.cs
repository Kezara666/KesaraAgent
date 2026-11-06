using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Agent.Api.Tests;

public sealed class DeviceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Nothing listens on this port. The tests use it to check behavior when a device does not answer.
    private const string UnreachableEndpoint = "http://127.0.0.1:9";

    [Fact]
    public async Task FR61RegisteredDeviceCanBeListedAndFetched()
    {
        using var client = AuthorizedClient();
        var id = $"lamp-{Guid.NewGuid():N}"[..20];

        var register = await client.PostAsJsonAsync("/api/v1/devices", Manifest(id));
        var get = await client.GetAsync($"/api/v1/devices/{id}");

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task FR61DuplicateDeviceIdIsAConflict()
    {
        using var client = AuthorizedClient();
        var id = $"dup-{Guid.NewGuid():N}"[..20];
        await client.PostAsJsonAsync("/api/v1/devices", Manifest(id));

        var second = await client.PostAsJsonAsync("/api/v1/devices", Manifest(id));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task FR61UnknownSafetyTierIsRefused()
    {
        using var client = AuthorizedClient();
        var manifest = new
        {
            id = $"bad-{Guid.NewGuid():N}"[..20],
            name = "Bad tier",
            endpoint = UnreachableEndpoint,
            capabilities = new[] { new { name = "x_action", action = "x_action", tier = "Dangerous" } },
        };

        var response = await client.PostAsJsonAsync("/api/v1/devices", manifest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FR61ReadOnlyCommandToAnUnreachableDeviceIsBadGateway()
    {
        using var client = AuthorizedClient();
        var id = $"gone-{Guid.NewGuid():N}"[..20];
        await client.PostAsJsonAsync("/api/v1/devices", Manifest(id));

        var response = await client.PostAsJsonAsync($"/api/v1/devices/{id}/commands", new { capability = "read_state", parameters = new Dictionary<string, string>() });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task FR61ReversibleCommandIsHeldAndFailedDeviceIsRecordedOnApproval()
    {
        using var client = AuthorizedClient();
        var id = $"held-{Guid.NewGuid():N}"[..20];
        await client.PostAsJsonAsync("/api/v1/devices", Manifest(id));

        var submit = await client.PostAsJsonAsync($"/api/v1/devices/{id}/commands", new { capability = "set_level", parameters = new Dictionary<string, string> { ["level"] = "40" } });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        var approvalId = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("approvalId").GetGuid();

        var approve = await client.PostAsync($"/api/v1/approvals/{approvalId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var body = await approve.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failed", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task FR61DeviceCommandForUnknownDeviceIsNotFound()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/v1/devices/no-such-device/commands", new { capability = "read_state" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static object Manifest(string id) => new
    {
        id,
        name = "Test lamp",
        endpoint = UnreachableEndpoint,
        capabilities = new[]
        {
            new { name = "read_state", action = "state", tier = "ReadOnly" },
            new { name = "set_level", action = "level", tier = "Reversible" },
        },
    };

    private HttpClient AuthorizedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.TestToken);
        return client;
    }
}
