using Agent.Core.Headless;
using Agent.Core.Llm;
using Agent.Core.Notifications;
using Agent.Infrastructure.Notifications;
using Agent.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Infrastructure;

public static class HeadlessServiceCollectionExtensions
{
    /// <summary>Registers scheduling, safe mode, and out-of-band notifications. Call after <c>AddAgentInfrastructure</c>.</summary>
    public static IServiceCollection AddAgentHeadless(this IServiceCollection services, string? ntfyBaseUrl, string? ntfyTopic)
    {
        services.AddSingleton<IScheduleStore, EfScheduleStore>();
        services.AddSingleton<IScheduledJobRunner, TaskJobRunner>();
        services.AddSingleton<SchedulerService>();
        services.AddSingleton<SafeModeController>();
        services.AddSingleton<IQuotaAlerts, NotifierQuotaAlerts>();

        if (string.IsNullOrWhiteSpace(ntfyTopic))
        {
            services.AddSingleton<INotifier, NullNotifier>();
        }
        else
        {
            var baseUrl = string.IsNullOrWhiteSpace(ntfyBaseUrl) ? "https://ntfy.sh/" : ntfyBaseUrl.TrimEnd('/') + "/";
            services.AddSingleton<INotifier>(_ => new NtfyNotifier(
                new HttpClient { BaseAddress = new Uri(baseUrl, UriKind.Absolute), Timeout = TimeSpan.FromSeconds(10) },
                ntfyTopic));
        }

        return services;
    }
}
