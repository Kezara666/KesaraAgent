using Agent.Core.ToolForge;
using Agent.Core.Workflows;
using ModelContextProtocol.Client;

namespace Agent.Infrastructure.Mcp;

/// <summary>
/// Connects to configured MCP servers and exposes their declared tools to workflows. A server that fails to
/// start is marked unavailable, and only its tools fail. Other tools keep working (FR-10.1, FR-10.5).
/// </summary>
public sealed class McpToolGateway : IToolCatalog, IToolInvoker, IToolListing, IAsyncDisposable
{
    private readonly Dictionary<string, Registration> tools = new(StringComparer.Ordinal);
    private readonly List<McpClient> clients = [];
    private readonly List<string> unavailableServers = [];

    private McpToolGateway()
    {
    }

    /// <summary>Servers that could not be reached. Reported to operators, never to the planner.</summary>
    public IReadOnlyList<string> UnavailableServers => unavailableServers;

    public static async Task<McpToolGateway> ConnectAsync(IEnumerable<McpServerOptions> servers, CancellationToken cancellationToken)
    {
        var gateway = new McpToolGateway();

        foreach (var server in servers)
        {
            await gateway.ConnectOneAsync(server, cancellationToken).ConfigureAwait(false);
        }

        return gateway;
    }

    public ToolDescriptor? Find(string name) => tools.TryGetValue(name, out var registration) ? registration.Descriptor : null;

    public IReadOnlyList<ToolDescriptor> List() => tools.Values.Select(registration => registration.Descriptor)
        .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
        .ToList();

    public async Task<string> InvokeAsync(string tool, IReadOnlyDictionary<string, string> inputs, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!tools.TryGetValue(tool, out var registration))
        {
            throw new InvalidOperationException("The tool is not available.");
        }

        var arguments = inputs.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
        var result = await registration.Client.CallToolAsync(registration.RemoteName, arguments, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.IsError == true)
        {
            throw new InvalidOperationException("The MCP tool reported an error.");
        }

        return string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(block => block.Text));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ConnectOneAsync(McpServerOptions server, CancellationToken cancellationToken)
    {
        try
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = server.Name,
                Command = server.Command,
                Arguments = server.Arguments,
                EnvironmentVariables = ServerEnvironment(server.Scopes),
            });

            var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
            clients.Add(client);

            var remote = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var remoteNames = remote.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var declared in server.Tools)
            {
                if (!remoteNames.Contains(declared.Name))
                {
                    continue;
                }

                var descriptor = new ToolDescriptor(
                    $"{server.Name}_{declared.Name}",
                    Enum.Parse<SideEffect>(declared.SideEffect, ignoreCase: true),
                    declared.RequiredInputs);

                tools[descriptor.Name] = new Registration(descriptor, client, declared.Name);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            unavailableServers.Add($"{server.Name}:{exception.GetType().Name}");
        }
    }

    /// <summary>
    /// The SDK inherits the host environment unless told otherwise, so every host variable is explicitly
    /// blanked. Only the variables needed to start a process are kept, plus the scope for this server.
    /// </summary>
    internal static Dictionary<string, string?> ServerEnvironment(string scopes)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = null;
        }

        foreach (var keep in AllowedHostVariables)
        {
            var value = Environment.GetEnvironmentVariable(keep);
            if (value is not null)
            {
                environment[keep] = value;
            }
        }

        environment["AGENT_MCP_SCOPES"] = scopes;
        return environment;
    }

    private static readonly string[] AllowedHostVariables = ["PATH", "SYSTEMROOT", "TEMP", "TMP"];

    private sealed record Registration(ToolDescriptor Descriptor, McpClient Client, string RemoteName);
}
