using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Persistence;

/// <summary>
/// Creates the schema and makes the audit table append-only at the storage layer (NFR-13).
/// Triggers reject UPDATE and DELETE even for code that bypasses the application.
/// </summary>
public static class DatabaseInitializer
{
    private const string AppendOnlyMessage = "audit log is append-only";

    public static async Task EnsureSchemaAsync(IDbContextFactory<AgentDbContext> factory, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER IF NOT EXISTS audit_no_update BEFORE UPDATE ON audit " +
            $"BEGIN SELECT RAISE(ABORT, '{AppendOnlyMessage}'); END;",
            cancellationToken).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER IF NOT EXISTS audit_no_delete BEFORE DELETE ON audit " +
            $"BEGIN SELECT RAISE(ABORT, '{AppendOnlyMessage}'); END;",
            cancellationToken).ConfigureAwait(false);
    }
}
