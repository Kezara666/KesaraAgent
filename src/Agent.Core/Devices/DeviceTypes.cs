namespace Agent.Core.Devices;

/// <summary>
/// How much harm a device action can do. Read-only actions run at once. Reversible and safety-critical actions wait for a
/// person to approve them. Safety-critical actions are marked as such to the approver (SRS safety tiers).
/// </summary>
public enum SafetyTier
{
    ReadOnly,
    Reversible,
    SafetyCritical,
}

/// <summary>One action a device offers. <c>Action</c> is the path segment sent to the device.</summary>
public sealed record DeviceCapability(string Name, string Action, SafetyTier Tier);

/// <summary>A device as registered: its identity, where to reach it, and what it can do. Nothing else is trusted.</summary>
public sealed record DeviceManifest(string Id, string Name, string Endpoint, IReadOnlyList<DeviceCapability> Capabilities);

/// <summary>Raised when a device id is not registered.</summary>
public sealed class DeviceNotFoundException(string id) : InvalidOperationException("The device does not exist.")
{
    public string Id { get; } = id;
}

/// <summary>Raised when a device does not answer or refuses an action. The message names the status, never a body.</summary>
public sealed class DeviceTransportException(string message) : InvalidOperationException(message);
