namespace Agent.Core.Approvals;

/// <summary>Port for approval requests. Adapters persist them so pending decisions survive restarts.</summary>
public interface IApprovalStore
{
    Task AddAsync(ApprovalRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(ApprovalRequest request, CancellationToken cancellationToken);

    Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApprovalRequest>> ListAsync(ApprovalStatus? status, int limit, CancellationToken cancellationToken);
}
