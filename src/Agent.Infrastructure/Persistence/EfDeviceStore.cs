using System.Text.Json;
using Agent.Core.Devices;
using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Persistence;

public sealed class EfDeviceStore(IDbContextFactory<AgentDbContext> factory) : IDeviceStore
{
    public async Task<bool> AddAsync(DeviceManifest device, string protectedSecret, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await db.Devices.AnyAsync(row => row.Id == device.Id, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        db.Devices.Add(new DeviceRow
        {
            Id = device.Id,
            Name = device.Name,
            Endpoint = device.Endpoint,
            CapabilitiesJson = JsonSerializer.Serialize(device.Capabilities),
            ProtectedSecret = protectedSecret,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<DeviceManifest?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Devices.AsNoTracking().FirstOrDefaultAsync(device => device.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<DeviceManifest>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Devices.AsNoTracking().OrderBy(device => device.Id).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<string?> GetProtectedSecretAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Devices.AsNoTracking()
            .Where(device => device.Id == id)
            .Select(device => device.ProtectedSecret)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static DeviceManifest ToRecord(DeviceRow row) => new(
        row.Id,
        row.Name,
        row.Endpoint,
        JsonSerializer.Deserialize<List<DeviceCapability>>(row.CapabilitiesJson)
            ?? throw new InvalidOperationException("The stored device capabilities could not be read."));
}
