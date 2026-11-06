using Agent.Core.Notifications;
using Agent.Infrastructure.Notifications;
using Xunit;

namespace Agent.Infrastructure.Tests;

public sealed class NotifierQuotaAlertsTests
{
    [Fact]
    public async Task NFR9QuotaAlertReportsCountsAndNoPromptOrReplyText()
    {
        var notifier = new CapturingNotifier();
        var alerts = new NotifierQuotaAlerts(notifier);

        await alerts.RaiseAsync("cloud-main", usedTokens: 85_000, budgetTokens: 100_000, CancellationToken.None);

        Assert.Equal("Model budget at 85%", notifier.Title);
        Assert.Equal("Profile cloud-main has used 85000 of 100000 tokens today.", notifier.Message);
    }

    private sealed class CapturingNotifier : INotifier
    {
        public string? Title { get; private set; }

        public string? Message { get; private set; }

        public Task NotifyAsync(string title, string message, CancellationToken cancellationToken)
        {
            Title = title;
            Message = message;
            return Task.CompletedTask;
        }
    }
}
