using Agent.Core.Llm;
using Agent.Core.Ports;
using Agent.Core.Shell;
using Agent.Core.Tasks;
using Agent.Core.Workspace;
using Agent.Infrastructure.Llm;
using Agent.Infrastructure.Persistence;
using Agent.Infrastructure.Queue;
using Agent.Infrastructure.Shell;
using Agent.Infrastructure.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Infrastructure;

public static class ServiceCollectionExtensions
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Registers the core services with their adapters: SQLite, in-process queue, local workspace,
    /// process shell, and the LLM router over the configured providers and profiles.
    /// </summary>
    public static IServiceCollection AddAgentInfrastructure(
        this IServiceCollection services,
        string sqliteConnectionString,
        string workspaceRoot,
        IReadOnlyList<ProviderOptions> providerOptions,
        IReadOnlyList<ModelProfileOptions> profileOptions)
    {
        services.AddDbContextFactory<AgentDbContext>(options => options.UseSqlite(sqliteConnectionString));

        services.AddSingleton<ITaskStore, EfTaskStore>();
        services.AddSingleton<ITaskDispatcher, ChannelTaskDispatcher>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TaskService>();
        services.AddSingleton<TaskProcessor>();

        services.AddSingleton<IUsageLedger, EfUsageLedger>();
        services.AddSingleton<ILlmClient>(serviceProvider => new LlmRouter(
            BuildProfiles(profileOptions),
            BuildProviders(providerOptions),
            serviceProvider.GetRequiredService<IUsageLedger>(),
            serviceProvider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IWorkspaceFileSystem>(_ => new LocalWorkspaceFileSystem(workspaceRoot));
        services.AddSingleton<IShellRunner>(_ => new ProcessShellRunner(Path.GetFullPath(workspaceRoot)));
        services.AddSingleton<WorkspaceService>();

        return services;
    }

    /// <summary>Configured profiles in order, with the offline profile always last so the system never stops.</summary>
    private static List<ModelProfile> BuildProfiles(IReadOnlyList<ModelProfileOptions> options)
    {
        var profiles = options.Select(profile => new ModelProfile(
                profile.Id,
                profile.ProviderId,
                profile.Model,
                profile.SupportsToolCalling,
                profile.ContextTokens,
                profile.DailyTokenBudget,
                profile.Enabled))
            .ToList();

        profiles.Add(new ModelProfile(
            Id: OfflineModelProvider.Id,
            ProviderId: OfflineModelProvider.Id,
            Model: OfflineModelProvider.Id,
            SupportsToolCalling: false,
            ContextTokens: 1_000_000,
            DailyTokenBudget: long.MaxValue,
            Enabled: true));

        return profiles;
    }

    private static List<IModelProvider> BuildProviders(IReadOnlyList<ProviderOptions> options)
    {
        var providers = new List<IModelProvider> { new OfflineModelProvider() };

        foreach (var option in options)
        {
            var http = new HttpClient
            {
                BaseAddress = new Uri(option.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
                Timeout = ProviderTimeout,
            };
            providers.Add(new OpenAiCompatibleProvider(option.Id, http, option.ApiKeyVariable));
        }

        return providers;
    }
}
