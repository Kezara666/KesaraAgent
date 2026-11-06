using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Agent.Api.Tests;

public sealed class WorkspaceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task FR32EditThroughApiReturnsDiffAndChangesFile()
    {
        Directory.CreateDirectory(factory.WorkspaceRoot);
        var relative = $"gate-{Guid.NewGuid():N}.md";
        var fullPath = Path.Combine(factory.WorkspaceRoot, relative);
        await File.WriteAllTextAsync(fullPath, "alpha\nbeta\n");

        using var client = AuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/v1/workspace/edits", new
        {
            path = relative,
            search = "beta",
            replace = "BETA",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("+BETA", body.GetProperty("diff").GetString(), StringComparison.Ordinal);
        Assert.Equal("alpha\nBETA\n", await File.ReadAllTextAsync(fullPath));
    }

    [Fact]
    public async Task FR34DeniedCommandThroughApiIsRefusedWith403()
    {
        using var client = AuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/v1/workspace/commands", new
        {
            command = "Remove-Item -Recurse -Force C:\\",
            shell = "powershell",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Denied", body.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task FR34AllowedCommandRunsInWorkspaceAndCapturesOutput()
    {
        Directory.CreateDirectory(factory.WorkspaceRoot);
        using var client = AuthorizedClient();

        const string shell = "bash";
        var response = await client.PostAsJsonAsync("/api/v1/workspace/commands", new
        {
            command = "echo gate-ok",
            shell,
            timeoutSeconds = 30,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("gate-ok", body.GetProperty("stdout").GetString(), StringComparison.Ordinal);
        Assert.False(body.GetProperty("timedOut").GetBoolean());
    }

    [Fact]
    public async Task FR31PathTraversalThroughEditIsRejectedAndNothingIsWritten()
    {
        Directory.CreateDirectory(factory.WorkspaceRoot);
        var outside = Path.Combine(Path.GetDirectoryName(factory.WorkspaceRoot)!, $"escape-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(outside, "untouched");
        using var client = AuthorizedClient();

        var response = await client.PostAsJsonAsync("/api/v1/workspace/edits", new
        {
            path = $"../{Path.GetFileName(outside)}",
            search = "untouched",
            replace = "changed",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("untouched", await File.ReadAllTextAsync(outside));
        File.Delete(outside);
    }

    [Fact]
    public async Task FR84WorkspaceEndpointsRequireAToken()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/workspace/search?query=x");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient AuthorizedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.TestToken);
        return client;
    }
}
