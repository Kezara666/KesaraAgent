namespace Agent.Infrastructure.Persistence;

public sealed class ScheduleRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string CronExpression { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    /// <summary>UTC ticks of the last submitted occurrence, or null if it has never run.</summary>
    public long? LastRunTicks { get; set; }
}
