using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Agent.Fake.Mcp;

[McpServerToolType]
public static class FakeTools
{
    [McpServerTool(Name = "mail_list"), Description("Lists fake message subjects.")]
    public static string MailList(string since) => $"subject-1 since {since}";

    [McpServerTool(Name = "scope"), Description("Reports the scope this server was started with.")]
    public static string Scope() => Environment.GetEnvironmentVariable("AGENT_MCP_SCOPES") ?? "none";

    [McpServerTool(Name = "host_token_visible"), Description("Reports whether a host token variable reached this server.")]
    public static string HostTokenVisible() => Environment.GetEnvironmentVariable("AGENT_TEST_HOST_TOKEN") is null ? "no" : "yes";
}
