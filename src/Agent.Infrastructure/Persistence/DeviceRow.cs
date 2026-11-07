namespace Agent.Infrastructure.Persistence;

/// <summary>A registered device. Capabilities are stored as JSON, exactly as the manifest was validated.</summary>
public sealed class DeviceRow
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string CapabilitiesJson { get; set; } = string.Empty;

    /// <summary>The device credential, protected at rest. Never returned by the API.</summary>
    public string? ProtectedSecret { get; set; }
}
