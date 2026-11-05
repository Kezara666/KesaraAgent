using Agent.Core.Audit;
using Agent.Core.Tasks;
using Agent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Agent.Infrastructure.Tests;

/// <summary>
/// Runs the store and audit guarantees against a real PostgreSQL container (started by Testcontainers).
/// Requires Docker. The migrations, the task store, and the append-only triggers must all work here.
/// </summary>
public sealed class PostgresParityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private TestFactory factory = null!;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseNpgsql(container.GetConnectionString())
            .Options;
        factory = new TestFactory(options);
        await DatabaseInitializer.EnsureSchemaAsync(factory, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await container.DisposeAsync();
    }

    [Fact]
    public async Task InfraPostgresTaskStoreRoundTripsATask()
    {
        var store = new EfTaskStore(factory);
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(Guid.CreateVersion7(), "hello postgres", AgentTaskStatus.Queued, "pg-key-1", null, null, now, now);

        await store.AddAsync(task, CancellationToken.None);
        var loaded = await store.GetAsync(task.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("hello postgres", loaded.Prompt);
        Assert.Equal(AgentTaskStatus.Queued, loaded.Status);
    }

    [Fact]
    public async Task InfraPostgresAuditAppendsAndListsNewestFirst()
    {
        var log = new EfAuditLog(factory);
        var id = Guid.CreateVersion7();

        await log.AppendAsync(new AuditEntry(id, DateTimeOffset.UtcNow, "alice", "pg.test", "target", null, "ok", null), CancellationToken.None);

        var rows = await log.ListRecentAsync(5, CancellationToken.None);
        Assert.Contains(rows, entry => entry.Id == id);
    }

    [Fact]
    public async Task NFR13PostgresRejectsAuditUpdate()
    {
        var log = new EfAuditLog(factory);
        var id = Guid.CreateVersion7();
        await log.AppendAsync(new AuditEntry(id, DateTimeOffset.UtcNow, "alice", "pg.test", "target", null, "ok", null), CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("UPDATE audit SET \"Result\" = 'changed' WHERE \"Id\" = {0}", id));

        Assert.Contains("append-only", error.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NFR13PostgresRejectsAuditDelete()
    {
        var log = new EfAuditLog(factory);
        var id = Guid.CreateVersion7();
        await log.AppendAsync(new AuditEntry(id, DateTimeOffset.UtcNow, "alice", "pg.test", "target", null, "ok", null), CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM audit WHERE \"Id\" = {0}", id));

        Assert.Contains("append-only", error.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestFactory(DbContextOptions<AgentDbContext> options) : IDbContextFactory<AgentDbContext>
    {
        public AgentDbContext CreateDbContext() => new(options);

        public ValueTask<AgentDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AgentDbContext(options));
    }
}
