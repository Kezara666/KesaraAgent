using System.Text.Json;
using Agent.Core.Approvals;
using Agent.Core.Audit;
using Agent.Core.Governance;

namespace Agent.Core.Devices;

/// <summary>
/// The only route to a device. Read-only actions run at once. Reversible and safety-critical actions are held for a
/// person, and a held action runs once, only if it is approved before it expires (SRS safety tiers, FR-3.4).
/// </summary>
public sealed class DeviceCommandService(
    IDeviceStore store,
    IDeviceTransport transport,
    IDeviceVault vault,
    IApprovalStore approvals,
    IAuditLog audit,
    TimeProvider clock)
{
    public const string ApprovalKind = "device.command";

    public static readonly TimeSpan ApprovalLifetime = TimeSpan.FromHours(24);

    private const int MaxResultCharacters = 4096;

    public async Task<DeviceCommandOutcome> RequestAsync(
        string deviceId,
        string capabilityName,
        IReadOnlyDictionary<string, string> parameters,
        string actor,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var device = await store.GetAsync(deviceId, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceNotFoundException(deviceId);
        var capability = device.Capabilities.FirstOrDefault(item => item.Name == capabilityName)
            ?? throw new ArgumentException("The device does not offer that capability.", nameof(capabilityName));

        if (capability.Tier == SafetyTier.ReadOnly)
        {
            var answer = await Send(device, capability, parameters, cancellationToken).ConfigureAwait(false);
            await AuditAsync(actor, "device.executed", Target(device, capability), "Executed", traceId, cancellationToken).ConfigureAwait(false);
            return new DeviceCommandOutcome.Executed(answer);
        }

        var now = clock.GetUtcNow();
        var payload = JsonSerializer.Serialize(new HeldCommand(device.Id, capability.Name, parameters));
        var request = new ApprovalRequest(
            Guid.CreateVersion7(),
            ApprovalKind,
            Target(device, capability),
            payload,
            ApprovalStatus.Pending,
            now,
            now.Add(ApprovalLifetime),
            DecidedAt: null,
            ResultSummary: null);

        await approvals.AddAsync(request, cancellationToken).ConfigureAwait(false);
        await AuditAsync(actor, "approval.requested", request.Target, "Pending", traceId, cancellationToken).ConfigureAwait(false);
        return new DeviceCommandOutcome.PendingApproval(request.Id, request.ExpiresAt);
    }

    public async Task<ApprovalRequest> DecideAsync(Guid approvalId, bool approve, string approver, CancellationToken cancellationToken)
    {
        var request = await approvals.GetAsync(approvalId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApprovalNotFoundException(approvalId);

        if (request.Kind != ApprovalKind || request.Status != ApprovalStatus.Pending)
        {
            throw new ApprovalStateException("The approval is not a pending device command.");
        }

        var now = clock.GetUtcNow();
        if (now > request.ExpiresAt)
        {
            await approvals.UpdateAsync(request with { Status = ApprovalStatus.Expired, DecidedAt = now }, cancellationToken)
                .ConfigureAwait(false);
            throw new ApprovalStateException("The approval has expired.");
        }

        if (!approve)
        {
            var rejected = request with { Status = ApprovalStatus.Rejected, DecidedAt = now };
            await approvals.UpdateAsync(rejected, cancellationToken).ConfigureAwait(false);
            await AuditAsync(approver, "approval.rejected", request.Target, "Rejected", null, cancellationToken).ConfigureAwait(false);
            return rejected;
        }

        var approved = request with { Status = ApprovalStatus.Approved, DecidedAt = now };
        await approvals.UpdateAsync(approved, cancellationToken).ConfigureAwait(false);
        await AuditAsync(approver, "approval.approved", request.Target, "Approved", null, cancellationToken).ConfigureAwait(false);

        var held = JsonSerializer.Deserialize<HeldCommand>(request.Payload)
            ?? throw new InvalidOperationException("The held device command could not be read.");
        var device = await store.GetAsync(held.DeviceId, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceNotFoundException(held.DeviceId);
        var capability = device.Capabilities.FirstOrDefault(item => item.Name == held.Capability)
            ?? throw new InvalidOperationException("The held capability is no longer offered by the device.");

        string summary;
        ApprovalStatus status;
        try
        {
            await Send(device, capability, held.Parameters, cancellationToken).ConfigureAwait(false);
            status = ApprovalStatus.Executed;
            summary = "Executed";
        }
        catch (DeviceTransportException exception)
        {
            status = ApprovalStatus.Failed;
            summary = exception.Message;
        }

        var finished = approved with { Status = status, DecidedAt = now, ResultSummary = Truncate(summary) };
        await approvals.UpdateAsync(finished, cancellationToken).ConfigureAwait(false);
        await AuditAsync(approver, "device.executed", request.Target, finished.ResultSummary!, null, cancellationToken).ConfigureAwait(false);
        return finished;
    }

    private async Task<string> Send(
        DeviceManifest device,
        DeviceCapability capability,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        // A device without a credential is never contacted. Such a device must be registered again.
        var protectedSecret = await store.GetProtectedSecretAsync(device.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceTransportException("The device has no credential. Register it again.");
        var credential = vault.Unprotect(protectedSecret);

        var answer = await transport.SendAsync(device, capability, parameters, credential, cancellationToken).ConfigureAwait(false);
        return answer.Length <= MaxResultCharacters ? answer : answer[..MaxResultCharacters];
    }

    private static string Target(DeviceManifest device, DeviceCapability capability) =>
        capability.Tier == SafetyTier.SafetyCritical
            ? $"{device.Id}:{capability.Name} [safety-critical]"
            : $"{device.Id}:{capability.Name}";

    private static string Truncate(string text) => text.Length <= 256 ? text : text[..256];

    private Task AuditAsync(string actor, string action, string target, string result, string? traceId, CancellationToken cancellationToken) =>
        audit.AppendAsync(
            new AuditEntry(Guid.CreateVersion7(), clock.GetUtcNow(), actor, action, target, Tier: null, result, traceId),
            cancellationToken);

    private sealed record HeldCommand(string DeviceId, string Capability, IReadOnlyDictionary<string, string> Parameters);
}

/// <summary>Outcome of a device request: an answer for read-only actions, or an approval to wait for.</summary>
public abstract record DeviceCommandOutcome
{
    public sealed record Executed(string Answer) : DeviceCommandOutcome;

    public sealed record PendingApproval(Guid ApprovalId, DateTimeOffset ExpiresAt) : DeviceCommandOutcome;
}
