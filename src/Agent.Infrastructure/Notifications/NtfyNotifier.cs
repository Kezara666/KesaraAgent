using System.Net.Http.Headers;
using System.Text;
using Agent.Core.Notifications;

namespace Agent.Infrastructure.Notifications;

/// <summary>
/// Sends alerts to a phone or desktop through an ntfy topic. Only the title and a short message are sent.
/// The topic name is the only access control on public ntfy, so treat it like a password.
/// </summary>
public sealed class NtfyNotifier(HttpClient http, string topic) : INotifier
{
    public async Task NotifyAsync(string title, string message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, topic)
        {
            Content = new StringContent(message, Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("Title", title);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Used when no topic is configured. Alerts are then only written to the audit log.</summary>
public sealed class NullNotifier : INotifier
{
    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken) => Task.CompletedTask;
}
