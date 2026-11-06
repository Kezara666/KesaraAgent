using System.Text.RegularExpressions;

namespace Agent.Core.Devices;

/// <summary>Checks a manifest before it is stored. A device that fails any check is not registered.</summary>
public static partial class DeviceRegistry
{
    public const int MaxCapabilities = 32;

    [GeneratedRegex("^[a-z][a-z0-9-]{2,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>Returns the problems found. An empty list means the manifest may be registered.</summary>
    public static IReadOnlyList<string> Validate(DeviceManifest device)
    {
        var problems = new List<string>();

        if (!IdPattern().IsMatch(device.Id ?? string.Empty))
        {
            problems.Add("The device id must be 3 to 41 lowercase letters, digits, or hyphens, starting with a letter.");
        }

        if (string.IsNullOrWhiteSpace(device.Name) || device.Name.Length > 64)
        {
            problems.Add("The device name must be 1 to 64 characters.");
        }

        if (!Uri.TryCreate(device.Endpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            problems.Add("The endpoint must be an absolute http or https address.");
        }

        if (device.Capabilities is null || device.Capabilities.Count == 0 || device.Capabilities.Count > MaxCapabilities)
        {
            problems.Add($"A device needs 1 to {MaxCapabilities} capabilities.");
        }
        else
        {
            foreach (var capability in device.Capabilities)
            {
                if (!NamePattern().IsMatch(capability.Name ?? string.Empty) || !NamePattern().IsMatch(capability.Action ?? string.Empty))
                {
                    problems.Add("Capability names and actions must be 2 to 41 lowercase letters, digits, or underscores.");
                }
            }

            var duplicates = device.Capabilities.GroupBy(capability => capability.Name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1);
            if (duplicates.Any())
            {
                problems.Add("Capability names must be unique on a device.");
            }
        }

        return problems;
    }
}
