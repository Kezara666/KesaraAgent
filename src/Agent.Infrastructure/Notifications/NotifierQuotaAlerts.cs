using Agent.Core.Llm;
using Agent.Core.Notifications;

namespace Agent.Infrastructure.Notifications;

/// <summary>Sends a quota warning through the configured notifier. The message carries counts only.</summary>
public sealed class NotifierQuotaAlerts(INotifier notifier) : IQuotaAlerts
{
    public Task RaiseAsync(string profileId, long usedTokens, long budgetTokens, CancellationToken cancellationToken)
    {
        var percent = budgetTokens <= 0 ? 100 : Math.Min(100, usedTokens * 100 / budgetTokens);
        return notifier.NotifyAsync(
            "Model budget at " + percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%",
            "Profile " + profileId + " has used " + usedTokens.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " of " + budgetTokens.ToString(System.Globalization.CultureInfo.InvariantCulture) + " tokens today.",
            cancellationToken);
    }
}
