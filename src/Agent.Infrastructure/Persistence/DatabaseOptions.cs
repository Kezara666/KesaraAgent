namespace Agent.Infrastructure.Persistence;

/// <summary>
/// Which database to use. <c>sqlite</c> is for local development and tests. <c>postgres</c> is the
/// target for everything else and is the only provider that runs versioned migrations.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SqliteProvider = "sqlite";
    public const string PostgresProvider = "postgres";

    public string Provider { get; set; } = SqliteProvider;

    public string ConnectionString { get; set; } = "Data Source=agent.db";

    public bool IsPostgres => string.Equals(Provider, PostgresProvider, StringComparison.OrdinalIgnoreCase);
}
