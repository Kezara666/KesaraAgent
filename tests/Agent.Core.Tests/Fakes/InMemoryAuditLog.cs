using Agent.Core.Audit;

namespace Agent.Core.Tests.Fakes;

internal sealed class InMemoryAuditLog : IAuditLog
{
    private readonly List<AuditEntry> entries = [];

    public IReadOnlyList<AuditEntry> Entries => entries;

    public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(entries.AsEnumerable().Reverse().Take(limit).ToList());
}
