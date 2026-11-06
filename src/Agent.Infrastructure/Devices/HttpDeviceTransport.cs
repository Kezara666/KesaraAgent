using System.Net.Http.Json;
using Agent.Core.Devices;

namespace Agent.Infrastructure.Devices;

/// <summary>
/// Sends an action to a device as an HTTP POST to <c>{endpoint}/{action}</c>, with the parameters as JSON. The
/// request has a short timeout. Errors name the status code only, never the device's response body.
/// </summary>
public sealed class HttpDeviceTransport(HttpClient http) : IDeviceTransport
{
    public async Task<string> SendAsync(
        DeviceManifest device,
        DeviceCapability capability,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string>(parameters, StringComparer.Ordinal);
        var url = $"{device.Endpoint.TrimEnd('/')}/{Uri.EscapeDataString(capability.Action)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new DeviceTransportException($"The device answered with status {(int)response.StatusCode}.");
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return text;
        }
        catch (HttpRequestException)
        {
            throw new DeviceTransportException("The device could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeviceTransportException("The device did not answer in time.");
        }
    }
}
