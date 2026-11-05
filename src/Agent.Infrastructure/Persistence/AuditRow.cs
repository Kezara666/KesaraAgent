namespace Agent.Infrastructure.Persistence;

public sealed class AuditRow
{
    public Guid Id { get; set; }

    public DateTimeOffset Time { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string? Tier { get; set; }

    public string Result { get; set; } = string.Empty;

    public string? TraceId { get; set; }
}
