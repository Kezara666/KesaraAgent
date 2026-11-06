using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// A stand-in MCP server for tests. It exposes a read-only mail list and two probes that report the
// scope and environment it was started with, so the integration tests can check what the core passes in.
var builder = Host.CreateApplicationBuilder(args);
// Stdout carries the JSON-RPC stream. Console logs there would corrupt the protocol, so logging is switched off.
builder.Logging.ClearProviders();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
