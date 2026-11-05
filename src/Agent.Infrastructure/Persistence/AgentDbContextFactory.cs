using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Agent.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>. Migrations are generated for PostgreSQL, the production provider.
/// The connection string is a placeholder and is never used to connect.
/// </summary>
public sealed class AgentDbContextFactory : IDesignTimeDbContextFactory<AgentDbContext>
{
    public AgentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseNpgsql("Host=localhost;Database=agent_design_time;Username=agent;Password=unused")
            .Options;
        return new AgentDbContext(options);
    }
}
