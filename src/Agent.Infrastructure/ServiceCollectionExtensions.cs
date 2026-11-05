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
    /// <summary>
    /// Registers the core services with their offline adapters (SQLite, in-process queue, echo LLM,
    /// local workspace, process shell).
    /// </summary>
    public static IServiceCollection AddAgentInfrastructure(
        this IServiceCollection services,
        string sqliteConnectionString,
        string workspaceRoot)
    {
        services.AddDbContextFactory<AgentDbContext>(options => options.UseSqlite(sqliteConnectionString));

        services.AddSingleton<ITaskStore, EfTaskStore>();
        services.AddSingleton<ITaskDispatcher, ChannelTaskDispatcher>();
        services.AddSingleton<ILlmClient, EchoLlmClient>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TaskService>();
        services.AddSingleton<TaskProcessor>();

        services.AddSingleton<IWorkspaceFileSystem>(_ => new LocalWorkspaceFileSystem(workspaceRoot));
        services.AddSingleton<IShellRunner>(_ => new ProcessShellRunner(Path.GetFullPath(workspaceRoot)));
        services.AddSingleton<WorkspaceService>();

        return services;
    }
}
