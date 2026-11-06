namespace Agent.Core.Devices;

/// <summary>Durable device registry.</summary>
public interface IDeviceStore
{
    /// <summary>Stores a new device. Returns false when the id is already registered.</summary>
    Task<bool> AddAsync(DeviceManifest device, CancellationToken cancellationToken);

    Task<DeviceManifest?> GetAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeviceManifest>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Sends one action to a device and returns its answer. Adapters enforce timeouts.</summary>
public interface IDeviceTransport
{
    Task<string> SendAsync(DeviceManifest device, DeviceCapability capability, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken);
}
