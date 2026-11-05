namespace Agent.Core.Notifications;

/// <summary>
/// Out-of-band channel for alerts and approval requests when no UI session is open (FR-7.4).
/// Messages must never contain secrets, prompts, or file contents.
/// </summary>
public interface INotifier
{
    Task NotifyAsync(string title, string message, CancellationToken cancellationToken);
}
