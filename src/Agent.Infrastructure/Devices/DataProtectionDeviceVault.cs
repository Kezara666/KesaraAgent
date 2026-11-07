using Agent.Core.Devices;
using Microsoft.AspNetCore.DataProtection;

namespace Agent.Infrastructure.Devices;

/// <summary>
/// Protects device credentials with the operating system's data protection (DPAPI on Windows). A stored credential can
/// only be read by the same user or service account on the same machine.
/// </summary>
public sealed class DataProtectionDeviceVault(IDataProtectionProvider provider) : IDeviceVault
{
    private const string Purpose = "Agent.Devices.Credentials";

    private readonly IDataProtector protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext) => protector.Protect(plaintext);

    public string Unprotect(string protectedText) => protector.Unprotect(protectedText);
}
