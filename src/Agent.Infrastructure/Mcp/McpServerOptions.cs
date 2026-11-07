namespace Agent.Infrastructure.Mcp;

/// <summary>
/// An MCP server the agent may start. Only tools listed under <see cref="Tools"/> are offered to the planner,
/// and each one must declare its side effect (FR-9.13, FR-10.3).
/// </summary>
public sealed class McpServerOptions
{
    public string Name { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public List<string> Arguments { get; set; } = [];

    /// <summary>
    /// The narrowest scope the server needs, for example <c>gmail.readonly</c>. Passed to the server process only.
    /// Tokens never pass through the agent; the server holds its own (FR-10.2, FR-10.4).
    /// </summary>
    public string Scopes { get; set; } = string.Empty;

    public List<McpToolOptions> Tools { get; set; } = [];
}

public sealed class McpToolOptions
{
    public string Name { get; set; } = string.Empty;

    /// <summary>ReadOnly, Write, or ExternalSend. Required, because promotion and approval depend on it.</summary>
    public string SideEffect { get; set; } = string.Empty;

    public List<string> RequiredInputs { get; set; } = [];

    /// <summary>Shown to the model. When empty, the server's own description is used.</summary>
    public string? Description { get; set; }
}
