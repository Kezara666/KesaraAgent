namespace Agent.Core.Audit;

/// <summary>
/// Append-only audit port. There are deliberately no update or delete members. Adapters must also
/// enforce this at the storage layer.
/// </summary>
public interface IAuditLog
{
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken cancellationToken);
}
