using Agent.Core.Approvals;
using Agent.Core.Devices;
using Agent.Core.Tests.Fakes;
using Xunit;

namespace Agent.Core.Tests.Devices;

public sealed class DeviceCommandServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    private static readonly DeviceManifest Lamp = new(
        "hall-lamp",
        "Hall lamp",
        "http://192.168.1.50",
        [
            new DeviceCapability("read_state", "state", SafetyTier.ReadOnly),
            new DeviceCapability("set_brightness", "brightness", SafetyTier.Reversible),
            new DeviceCapability("unlock_door", "unlock", SafetyTier.SafetyCritical),
        ]);

    [Fact]
    public async Task FR61ReadOnlyCapabilityRunsAtOnceWithoutApproval()
    {
        var transport = new RecordingTransport("on");
        var approvals = new InMemoryApprovalStore();
        var service = Build(transport, approvals);

        var outcome = await service.RequestAsync("hall-lamp", "read_state", new Dictionary<string, string>(), "alice", null, CancellationToken.None);

        var executed = Assert.IsType<DeviceCommandOutcome.Executed>(outcome);
        Assert.Equal("on", executed.Answer);
        Assert.Equal(1, transport.Calls);
        Assert.Empty(await approvals.ListAsync(null, 10, CancellationToken.None));
    }

    [Fact]
    public async Task FR61ReversibleCapabilityIsHeldUntilApprovedThenRunsOnce()
    {
        var transport = new RecordingTransport();
        var approvals = new InMemoryApprovalStore();
        var service = Build(transport, approvals);
        var pending = Assert.IsType<DeviceCommandOutcome.PendingApproval>(
            await service.RequestAsync("hall-lamp", "set_brightness", new Dictionary<string, string> { ["level"] = "40" }, "alice", null, CancellationToken.None));
        Assert.Equal(0, transport.Calls);

        var decided = await service.DecideAsync(pending.ApprovalId, approve: true, "approver", CancellationToken.None);

        Assert.Equal(ApprovalStatus.Executed, decided.Status);
        Assert.Equal(1, transport.Calls);
        Assert.Equal("level", Assert.Single(transport.LastParameters).Key);
    }

    [Fact]
    public async Task FR61SafetyCriticalRequestIsMarkedForTheApprover()
    {
        var approvals = new InMemoryApprovalStore();
        var service = Build(new RecordingTransport(), approvals);

        await service.RequestAsync("hall-lamp", "unlock_door", new Dictionary<string, string>(), "alice", null, CancellationToken.None);

        var held = Assert.Single(await approvals.ListAsync(ApprovalStatus.Pending, 10, CancellationToken.None));
        Assert.Equal("hall-lamp:unlock_door [safety-critical]", held.Target);
        Assert.Equal(DeviceCommandService.ApprovalKind, held.Kind);
    }

    [Fact]
    public async Task FR61RejectedDeviceCommandNeverRuns()
    {
        var transport = new RecordingTransport();
        var service = Build(transport, new InMemoryApprovalStore());
        var pending = Assert.IsType<DeviceCommandOutcome.PendingApproval>(
            await service.RequestAsync("hall-lamp", "unlock_door", new Dictionary<string, string>(), "alice", null, CancellationToken.None));

        var decided = await service.DecideAsync(pending.ApprovalId, approve: false, "approver", CancellationToken.None);

        Assert.Equal(ApprovalStatus.Rejected, decided.Status);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task FR61FailedDeviceIsRecordedAsFailedNotThrown()
    {
        var transport = new RecordingTransport(failWith: "The device could not be reached.");
        var service = Build(transport, new InMemoryApprovalStore());
        var pending = Assert.IsType<DeviceCommandOutcome.PendingApproval>(
            await service.RequestAsync("hall-lamp", "set_brightness", new Dictionary<string, string>(), "alice", null, CancellationToken.None));

        var decided = await service.DecideAsync(pending.ApprovalId, approve: true, "approver", CancellationToken.None);

        Assert.Equal(ApprovalStatus.Failed, decided.Status);
        Assert.Equal("The device could not be reached.", decided.ResultSummary);
    }

    [Fact]
    public async Task FR61UnknownDeviceAndCapabilityAreRefused()
    {
        var transport = new RecordingTransport();
        var service = Build(transport, new InMemoryApprovalStore());

        await Assert.ThrowsAsync<DeviceNotFoundException>(() =>
            service.RequestAsync("no-such-device", "read_state", new Dictionary<string, string>(), "alice", null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RequestAsync("hall-lamp", "fly", new Dictionary<string, string>(), "alice", null, CancellationToken.None));
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public void FR61ManifestWithBadEndpointOrDuplicateCapabilityIsRefused()
    {
        var badScheme = Lamp with { Endpoint = "ftp://192.168.1.50" };
        var duplicate = Lamp with
        {
            Capabilities = [new DeviceCapability("same", "a", SafetyTier.ReadOnly), new DeviceCapability("same", "b", SafetyTier.ReadOnly)],
        };

        Assert.NotEmpty(DeviceRegistry.Validate(badScheme));
        Assert.NotEmpty(DeviceRegistry.Validate(duplicate));
        Assert.Empty(DeviceRegistry.Validate(Lamp));
    }

    [Fact]
    public async Task FR61TheDeviceCredentialIsSentWithTheCommand()
    {
        var transport = new RecordingTransport("on");
        var service = Build(transport, new InMemoryApprovalStore());

        await service.RequestAsync("hall-lamp", "read_state", new Dictionary<string, string>(), "alice", null, CancellationToken.None);

        Assert.Equal(Credential, transport.LastCredential);
    }

    [Fact]
    public async Task FR61DeviceWithoutACredentialIsNeverContacted()
    {
        var transport = new RecordingTransport("on");
        var store = new InMemoryDeviceStore();
        await store.AddAsync(Lamp, protectedSecret: null!, CancellationToken.None);
        var service = new DeviceCommandService(store, transport, new PrefixVault(), new InMemoryApprovalStore(), new InMemoryAuditLog(), new FakeClock(Start));

        await Assert.ThrowsAsync<DeviceTransportException>(() =>
            service.RequestAsync("hall-lamp", "read_state", new Dictionary<string, string>(), "alice", null, CancellationToken.None));
        Assert.Equal(0, transport.Calls);
    }

    private static DeviceCommandService Build(IDeviceTransport transport, InMemoryApprovalStore approvals)
    {
        var store = new InMemoryDeviceStore();
        store.AddAsync(Lamp, "protected:" + Credential, CancellationToken.None).GetAwaiter().GetResult();
        return new DeviceCommandService(store, transport, new PrefixVault(), approvals, new InMemoryAuditLog(), new FakeClock(Start));
    }

    private const string Credential = "lamp-credential";

    private sealed class PrefixVault : IDeviceVault
    {
        public string Protect(string plaintext) => "protected:" + plaintext;

        public string Unprotect(string protectedText) => protectedText["protected:".Length..];
    }

    private sealed class RecordingTransport(string answer = "ok", string? failWith = null) : IDeviceTransport
    {
        public int Calls { get; private set; }

        public IReadOnlyDictionary<string, string> LastParameters { get; private set; } = new Dictionary<string, string>();

        public string? LastCredential { get; private set; }

        public Task<string> SendAsync(
            DeviceManifest device,
            DeviceCapability capability,
            IReadOnlyDictionary<string, string> parameters,
            string credential,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastParameters = parameters;
            LastCredential = credential;
            if (failWith is not null)
            {
                throw new DeviceTransportException(failWith);
            }

            return Task.FromResult(answer);
        }
    }

    private sealed class InMemoryDeviceStore : IDeviceStore
    {
        private readonly Dictionary<string, DeviceManifest> devices = new(StringComparer.Ordinal);

        private readonly Dictionary<string, string> secrets = new(StringComparer.Ordinal);

        public Task<bool> AddAsync(DeviceManifest device, string protectedSecret, CancellationToken cancellationToken)
        {
            var added = devices.TryAdd(device.Id, device);
            if (added)
            {
                secrets[device.Id] = protectedSecret;
            }

            return Task.FromResult(added);
        }

        public Task<string?> GetProtectedSecretAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(secrets.TryGetValue(id, out var secret) ? secret : null);

        public Task<DeviceManifest?> GetAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(devices.TryGetValue(id, out var device) ? device : null);

        public Task<IReadOnlyList<DeviceManifest>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeviceManifest>>(devices.Values.ToList());
    }
}
