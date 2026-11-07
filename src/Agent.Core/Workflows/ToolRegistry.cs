namespace Agent.Core.Workflows;

/// <summary>A source of tools: built-in tools, MCP servers, and anything else that can be listed, found, and run.</summary>
public interface IToolProvider : IToolCatalog, IToolListing, IToolInvoker
{
}

/// <summary>
/// One view over every tool source. The planner, workflows, and the agent loop all use this, so each sees the same
/// tools and the same rules. The first source that declares a name owns it.
/// </summary>
public sealed class ToolRegistry(IEnumerable<IToolProvider> providers) : IToolCatalog, IToolListing, IToolInvoker
{
    private readonly IReadOnlyList<IToolProvider> sources = providers.ToList();

    public ToolDescriptor? Find(string name)
    {
        foreach (var source in sources)
        {
            var found = source.Find(name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    public IReadOnlyList<ToolDescriptor> List() => sources
        .SelectMany(source => source.List())
        .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
        .ToList();

    public Task<string> InvokeAsync(string tool, IReadOnlyDictionary<string, string> inputs, string idempotencyKey, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            if (source.Find(tool) is not null)
            {
                return source.InvokeAsync(tool, inputs, idempotencyKey, cancellationToken);
            }
        }

        throw new InvalidOperationException("The tool is not available.");
    }
}
