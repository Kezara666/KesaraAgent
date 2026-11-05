using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Agent.Api.Tests;

public sealed class TokenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] ReadOnlyScopes = ["read"];
    private static readonly string[] AdminScopes = ["admin"];
    private static readonly string[] UnknownScopes = ["superuser"];

    [Fact]
    public async Task FR84IssuedTokenWorksOnlyWithItsScopes()
    {
        using var admin = AdminClient();
        var issued = await CreateTokenAsync(admin, scopes: ReadOnlyScopes);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", issued);

        var read = await client.GetAsync("/api/v1/tasks?limit=1");
        var write = await client.PostAsJsonAsync("/api/v1/tasks", new { prompt = "not allowed" });

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task FR84RevokedTokenIsRefusedFromTheNextRequest()
    {
        using var admin = AdminClient();
        var (id, raw) = await CreateTokenWithIdAsync(admin, scopes: ReadOnlyScopes);

        var revoke = await admin.PostAsync($"/api/v1/tokens/{id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", raw);
        var response = await client.GetAsync("/api/v1/tasks?limit=1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FR84TokenListNeverShowsRawTokenOrHash()
    {
        using var admin = AdminClient();
        await CreateTokenAsync(admin, scopes: ReadOnlyScopes);

        var list = await admin.GetAsync("/api/v1/tokens");
        var text = await list.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain("tokenHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"token\"", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FR84NonAdminCannotCreateTokens()
    {
        using var admin = AdminClient();
        var readOnly = await CreateTokenAsync(admin, scopes: ReadOnlyScopes);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", readOnly);
        var response = await client.PostAsJsonAsync("/api/v1/tokens", new { name = "escalate", scopes = AdminScopes });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FR84TokenWithAnUnknownScopeIsRefused()
    {
        using var admin = AdminClient();

        var response = await admin.PostAsJsonAsync("/api/v1/tokens", new { name = "bad", scopes = UnknownScopes });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient AdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.TestToken);
        return client;
    }

    private static async Task<string> CreateTokenAsync(HttpClient admin, string[] scopes) =>
        (await CreateTokenWithIdAsync(admin, scopes)).Raw;

    private static async Task<(Guid Id, string Raw)> CreateTokenWithIdAsync(HttpClient admin, string[] scopes)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/tokens", new { name = $"client-{Guid.NewGuid():N}", scopes });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("token").GetString()!);
    }
}
