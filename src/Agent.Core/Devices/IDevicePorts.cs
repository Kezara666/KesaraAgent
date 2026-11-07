namespace Agent.Core.Devices;

/// <summary>Durable device registry. Secrets are stored protected, never in plain text.</summary>
public interface IDeviceStore
{
    /// <summary>Stores a new device with its protected secret. Returns false when the id is already registered.</summary>
    Task<bool> AddAsync(DeviceManifest device, string protectedSecret, CancellationToken cancellationToken);

    Task<DeviceManifest?> GetAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeviceManifest>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The protected secret for a device, or null when the device has none.</summary>
    Task<string?> GetProtectedSecretAsync(string id, CancellationToken cancellationToken);
}

/// <summary>Protects device secrets at rest. Adapters use the operating system's data protection.</summary>
public interface IDeviceVault
{
    string Protect(string plaintext);

    string Unprotect(string protectedText);
}

/// <summary>Sends one action to a device and returns its answer. Adapters enforce timeouts and send the credential.</summary>
public interface IDeviceTransport
{
    Task<string> SendAsync(
        DeviceManifest device,
        DeviceCapability capability,
        IReadOnlyDictionary<string, string> parameters,
        string credential,
        CancellationToken cancellationToken);
}
