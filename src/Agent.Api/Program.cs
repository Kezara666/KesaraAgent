using Agent.Api.Auditing;
using Agent.Api.Auth;
using Agent.Api.Endpoints;
using Agent.Api.Workers;
using Agent.Core.Tasks;
using Agent.Infrastructure;
using Agent.Infrastructure.Llm;
using Agent.Infrastructure.Persistence;
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
builder.Services.AddAgentInfrastructure(database, workspaceRoot, providerOptions, profileOptions);
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
app.MapAuditEndpoints();
app.MapScheduleEndpoints();
app.MapTokenEndpoints();

app.Run();

// Exposed for the integration tests in Agent.Api.Tests.
public partial class Program { }
