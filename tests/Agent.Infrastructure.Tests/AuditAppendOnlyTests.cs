using Agent.Core.Audit;
using Agent.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Agent.Infrastructure.Tests;

public sealed class AuditAppendOnlyTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"agent-audit-{Guid.NewGuid():N}.db");
    private readonly TestFactory factory;

    public AuditAppendOnlyTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        factory = new TestFactory(options);
    }

    public async Task InitializeAsync() =>
        await DatabaseInitializer.EnsureSchemaAsync(factory, CancellationToken.None);

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        TryDelete(databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task NFR13AuditRowCanBeAppendedAndReadBack()
    {
        var log = new EfAuditLog(factory);
        var id = Guid.CreateVersion7();

        await log.AppendAsync(new AuditEntry(id, DateTimeOffset.UtcNow, "alice", "test.action", "target", null, "ok", null), CancellationToken.None);

        var rows = await log.ListRecentAsync(10, CancellationToken.None);
        Assert.Contains(rows, entry => entry.Id == id);
    }

    [Fact]
    public async Task NFR13AuditRowCannotBeUpdated()
    {
        var log = new EfAuditLog(factory);
        var id = Guid.CreateVersion7();
        await log.AppendAsync(new AuditEntry(id, DateTimeOffset.UtcNow, "alice", "test.action", "target", null, "ok", null), CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var error = Assert.Throws<SqliteException>(() => ExecuteRaw(db, "UPDATE audit SET Result = 'changed' WHERE Id = {0}", id));

        Assert.Contains("append-only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void ExecuteRaw(AgentDbContext db, string sql, Guid id) =>
        db.Database.ExecuteSqlRaw(sql, id);

    [Fact]
    public async Task NFR13AuditRowCannotBeDeleted()
    {
        var log = new EfAuditLog(factory);
        var id = Guid.CreateVersion7();
        await log.AppendAsync(new AuditEntry(id, DateTimeOffset.UtcNow, "alice", "test.action", "target", null, "ok", null), CancellationToken.None);

        await using var db = await factory.CreateDbContextAsync();
        var error = Assert.Throws<SqliteException>(() => ExecuteRaw(db, "DELETE FROM audit WHERE Id = {0}", id));

        Assert.Contains("append-only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Released later by the OS temp cleanup.
        }
    }

    private sealed class TestFactory(DbContextOptions<AgentDbContext> options) : IDbContextFactory<AgentDbContext>
    {
        public AgentDbContext CreateDbContext() => new(options);

        public ValueTask<AgentDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AgentDbContext(options));
    }
}
