using Agent.Core.Ports;
using Agent.Core.Tasks;
using Agent.Infrastructure.Llm;
using Agent.Infrastructure.Persistence;
using Agent.Infrastructure.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the core services with their offline adapters (SQLite, in-process queue, echo LLM).</summary>
    public static IServiceCollection AddAgentInfrastructure(this IServiceCollection services, string sqliteConnectionString)
    {
        services.AddDbContextFactory<AgentDbContext>(options => options.UseSqlite(sqliteConnectionString));

        services.AddSingleton<ITaskStore, EfTaskStore>();
        services.AddSingleton<ITaskDispatcher, ChannelTaskDispatcher>();
        services.AddSingleton<ILlmClient, EchoLlmClient>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TaskService>();
        services.AddSingleton<TaskProcessor>();

        return services;
    }
}
