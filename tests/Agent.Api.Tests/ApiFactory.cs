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

    public static string TestTokenHash =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TestToken))).ToLowerInvariant();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Agent:DatabasePath", databasePath);
        builder.UseSetting("Agent:ApiTokenSha256", TestTokenHash);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(databasePath))
        {
            try
            {
                File.Delete(databasePath);
            }
            catch (IOException)
            {
                // SQLite may still hold the file briefly. The OS temp folder is cleaned later.
            }
        }
    }
}
