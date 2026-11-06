using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Agent.Api.Tests;

/// <summary>
/// Runs the real API with a temporary SQLite file and a known test token.
/// No UI and no LLM provider are involved.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestToken = "test-token-for-integration-only";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"agent-tests-{Guid.NewGuid():N}.db");

    /// <summary>A throwaway workspace the tests can write files into before calling the API.</summary>
    public string WorkspaceRoot { get; } = Path.Combine(Path.GetTempPath(), $"agent-ws-{Guid.NewGuid():N}");

    public static string TestTokenHash =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TestToken))).ToLowerInvariant();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // AGENT_TEST_POSTGRES runs the whole API suite against a PostgreSQL server instead of SQLite.
        var postgres = Environment.GetEnvironmentVariable("AGENT_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(postgres))
        {
            builder.UseSetting("Agent:Database:Provider", "postgres");
            builder.UseSetting("Agent:Database:ConnectionString", postgres);
        }
        else
        {
            builder.UseSetting("Agent:DatabasePath", databasePath);
        }


        builder.UseSetting("Agent:ApiTokenSha256", TestTokenHash);
        builder.UseSetting("Agent:WorkspaceRoot", WorkspaceRoot);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        TryDelete(() => File.Delete(databasePath));
        TryDelete(() => Directory.Delete(WorkspaceRoot, recursive: true));
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (IOException)
        {
            // A handle may still be open briefly. The OS temp folder is cleaned later.
        }
    }
}
