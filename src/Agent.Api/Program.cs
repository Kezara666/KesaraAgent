using Agent.Api.Auditing;
using Agent.Api.Auth;
using Agent.Api.Endpoints;
using Agent.Api.Workers;
using Agent.Core.Approvals;
using Agent.Core.Sandbox;
using Agent.Core.Shell;
using Agent.Core.Tasks;
using Agent.Core.ToolForge;
using Agent.Core.Workflows;
using Agent.Infrastructure;
using Agent.Infrastructure.Llm;
using Agent.Infrastructure.Mcp;
using Agent.Infrastructure.Persistence;
using Agent.Sandbox;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var database = new DatabaseOptions
{
    Provider = builder.Configuration["Agent:Database:Provider"] ?? DatabaseOptions.SqliteProvider,
    ConnectionString = builder.Configuration["Agent:Database:ConnectionString"]
        ?? $"Data Source={builder.Configuration["Agent:DatabasePath"] ?? "agent.db"}",
};
var workspaceRoot = builder.Configuration["Agent:WorkspaceRoot"] ?? "workspace";
var providerOptions = builder.Configuration.GetSection("Agent:Providers").Get<List<ProviderOptions>>() ?? [];
var profileOptions = builder.Configuration.GetSection("Agent:ModelProfiles").Get<List<ModelProfileOptions>>() ?? [];
var toolsRoot = builder.Configuration["Agent:Tools:Root"] ?? "tools";
builder.Services.AddAgentInfrastructure(database, workspaceRoot, providerOptions, profileOptions, toolsRoot);

// Shell commands run in the Docker sandbox, never on the host. Images are configured and allowlisted.
var bashImage = builder.Configuration["Agent:Sandbox:BashImage"] ?? "alpine:3.20";
var powerShellImage = builder.Configuration["Agent:Sandbox:PowerShellImage"];
var toolTestImage = builder.Configuration["Agent:Sandbox:ToolTestImage"] ?? "mcr.microsoft.com/dotnet/sdk:10.0-alpine";
var allowedImages = new HashSet<string>(StringComparer.Ordinal) { bashImage, toolTestImage };
if (!string.IsNullOrWhiteSpace(powerShellImage))
{
    allowedImages.Add(powerShellImage);
}
builder.Services.AddSingleton<ISandboxRunner>(_ => new DockerSandboxRunner(allowedImages));
builder.Services.AddSingleton<IShellRunner>(provider => new SandboxShellRunner(
    provider.GetRequiredService<ISandboxRunner>(),
    Path.GetFullPath(workspaceRoot),
    bashImage,
    powerShellImage));
// Tools for plans: the configured MCP servers, plus the sandboxed test runner for generated tools.
// Only tools declared in configuration are offered to the planner (FR-9.13).
var mcpServers = builder.Configuration.GetSection("Agent:McpServers").Get<List<McpServerOptions>>() ?? [];
var mcpGateway = await McpToolGateway.ConnectAsync(mcpServers, CancellationToken.None);
builder.Services.AddSingleton(mcpGateway);
builder.Services.AddSingleton<IToolCatalog>(mcpGateway);
builder.Services.AddSingleton<IToolListing>(mcpGateway);
builder.Services.AddSingleton<IToolInvoker>(mcpGateway);
builder.Services.AddSingleton<IToolTestRunner>(provider => new SandboxToolTestRunner(
    provider.GetRequiredService<ISandboxRunner>(),
    toolTestImage,
    Path.Combine(Directory.GetCurrentDirectory(), "tools", "staging")));
builder.Services.AddSingleton<WorkflowRunner>();
builder.Services.AddSingleton<WorkflowService>();
builder.Services.AddSingleton<ToolForgeService>();
builder.Services.AddSingleton<ApprovalDecisionRouter>();

builder.Services.AddAgentHeadless(
    builder.Configuration["Agent:Notifications:NtfyBaseUrl"],
    builder.Configuration["Agent:Notifications:NtfyTopic"]);
builder.Services.AddWindowsService();
builder.Services.AddHostedService<SchedulerWorker>();

// Traces and metrics for every request, LLM call, and database call. Export is switched on by
// OTEL_EXPORTER_OTLP_ENDPOINT, so tests and offline runs send nothing anywhere.
var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("kesara-agent-api"))
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation());

if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
{
    otel.UseOtlpExporter();
}

builder.Services.AddAuthentication(ApiTokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(ApiTokenAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ApiPolicies.Read, policy => policy.RequireClaim(ApiTokenAuthenticationHandler.ScopeClaimType, ApiScopes.Read, ApiScopes.Act, ApiScopes.Admin))
    .AddPolicy(ApiPolicies.Act, policy => policy.RequireClaim(ApiTokenAuthenticationHandler.ScopeClaimType, ApiScopes.Act, ApiScopes.Admin))
    .AddPolicy(ApiPolicies.Admin, policy => policy.RequireClaim(ApiTokenAuthenticationHandler.ScopeClaimType, ApiScopes.Admin));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHostedService<TaskWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AgentDbContext>>();
    await DatabaseInitializer.EnsureSchemaAsync(factory, CancellationToken.None);
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseMiddleware<ApiAuditMiddleware>();
app.UseAuthorization();

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Agent API v1");
    options.RoutePrefix = "docs";
});

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();

app.MapGet("/health/ready", async (TaskService service, CancellationToken cancellationToken) =>
    await service.IsStoreAvailableAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(title: "Store unavailable", statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

app.MapTaskEndpoints();
app.MapWorkspaceEndpoints();
app.MapApprovalEndpoints();
app.MapWorkflowEndpoints();
app.MapToolEndpoints();
app.MapAuditEndpoints();
app.MapScheduleEndpoints();
app.MapTokenEndpoints();

app.Run();

// Exposed for the integration tests in Agent.Api.Tests.
public partial class Program { }
