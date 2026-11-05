using Agent.Api.Auth;
using Agent.Api.Endpoints;
using Agent.Api.Workers;
using Agent.Core.Tasks;
using Agent.Infrastructure;
using Agent.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var databasePath = builder.Configuration["Agent:DatabasePath"] ?? "agent.db";
var workspaceRoot = builder.Configuration["Agent:WorkspaceRoot"] ?? "workspace";
builder.Services.AddAgentInfrastructure($"Data Source={databasePath}", workspaceRoot);

builder.Services.AddAuthentication(ApiTokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(ApiTokenAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ApiPolicies.Read, policy => policy.RequireClaim(ApiTokenAuthenticationHandler.ScopeClaimType, ApiScopes.Read, ApiScopes.Act, ApiScopes.Admin))
    .AddPolicy(ApiPolicies.Act, policy => policy.RequireClaim(ApiTokenAuthenticationHandler.ScopeClaimType, ApiScopes.Act, ApiScopes.Admin));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHostedService<TaskWorker>();

var app = builder.Build();

// Schema is created on startup for this branch. Versioned migrations arrive with infra/postgres.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AgentDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
}

app.UseExceptionHandler();
app.UseAuthentication();
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

app.Run();

// Exposed for the integration tests in Agent.Api.Tests.
public partial class Program { }
