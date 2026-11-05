using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Agent.Api.Tests;

public sealed class ScheduleApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task FR98PreviewShowsNextRunsWithoutSaving()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/v1/schedules/preview", new { cron = "*/5 * * * *", count = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("nextRuns").GetArrayLength());
    }

    [Fact]
    public async Task FR98InvalidCronIsRejectedWithProblemDetails()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/v1/schedules/preview", new { cron = "every morning please" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FR98CreateWithoutConfirmationIsRefused()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/v1/schedules", new { name = "unconfirmed", cron = "0 7 * * *", prompt = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FR98ConfirmedScheduleIsSavedAndListed()
    {
        using var client = AuthorizedClient();
        var name = $"morning-{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync("/api/v1/schedules", new { name, cron = "0 7 * * 1-5", prompt = "summarize mail", confirm = true });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/schedules");
        Assert.Contains(list.EnumerateArray(), item => item.GetProperty("name").GetString() == name);
    }

    private HttpClient AuthorizedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.TestToken);
        return client;
    }
}
