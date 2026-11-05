using Agent.Core.Approvals;

namespace Agent.Core.Tests.Fakes;

internal sealed class InMemoryApprovalStore : IApprovalStore
{
    private readonly Dictionary<Guid, ApprovalRequest> items = [];

    public Task AddAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        items[request.Id] = request;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        items[request.Id] = request;
        return Task.CompletedTask;
    }

    public Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(items.GetValueOrDefault(id));

    public Task<IReadOnlyList<ApprovalRequest>> ListAsync(ApprovalStatus? status, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApprovalRequest>>(items.Values
            .Where(item => status is null || item.Status == status)
            .Take(limit)
            .ToList());
}
