using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Agent.Infrastructure.Persistence;

/// <summary>
/// Brings the schema up to date and makes the audit table append-only at the storage layer (NFR-13).
/// PostgreSQL uses versioned migrations. SQLite (development and tests) uses EnsureCreated.
/// Triggers reject UPDATE and DELETE even for code that bypasses the application.
/// </summary>
public static class DatabaseInitializer
{
    private const string AppendOnlyMessage = "audit log is append-only";

    public static async Task EnsureSchemaAsync(IDbContextFactory<AgentDbContext> factory, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (db.Database.IsNpgsql())
        {
            await MigrateWithLockAsync(db, cancellationToken).ConfigureAwait(false);
            await ApplyPostgresAppendOnlyAsync(db, cancellationToken).ConfigureAwait(false);
            return;
        }

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

    /// <summary>
    /// Several processes can start at once (for example, parallel test hosts or a service restart).
    /// A session-level advisory lock makes exactly one of them run migrations at a time.
    /// </summary>
    private static async Task MigrateWithLockAsync(AgentDbContext db, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var acquire = new NpgsqlCommand("SELECT pg_advisory_lock(918273645)", connection))
        {
            await acquire.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(918273645)", connection);
            await release.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task ApplyPostgresAppendOnlyAsync(AgentDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            "CREATE OR REPLACE FUNCTION audit_append_only() RETURNS trigger AS $$ " +
            $"BEGIN RAISE EXCEPTION '{AppendOnlyMessage}'; END; " +
            "$$ LANGUAGE plpgsql;",
            cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS audit_no_update ON audit; " +
            "CREATE TRIGGER audit_no_update BEFORE UPDATE ON audit FOR EACH ROW EXECUTE FUNCTION audit_append_only();",
            cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS audit_no_delete ON audit; " +
            "CREATE TRIGGER audit_no_delete BEFORE DELETE ON audit FOR EACH ROW EXECUTE FUNCTION audit_append_only();",
            cancellationToken).ConfigureAwait(false);
    }
}
