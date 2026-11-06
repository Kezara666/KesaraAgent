using Agent.Core.ToolForge;
using Agent.Core.Workflows;
using Agent.Infrastructure.Mcp;
using Xunit;

namespace Agent.Infrastructure.Tests;

/// <summary>Runs the fake MCP server as a child process over stdio and checks the real protocol path.</summary>
public sealed class McpToolGatewayTests
{
    private const string HostTokenVariable = "AGENT_TEST_HOST_TOKEN";

    private static readonly string FakeServerPath = Path.Combine(AppContext.BaseDirectory, "Agent.Fake.Mcp.dll");

    [Fact]
    public async Task FR101DeclaredToolsAreDiscoveredWithTheirSideEffect()
    {
        await using var gateway = await ConnectAsync(FakeServer());

        var descriptor = gateway.Find("fake_mail_list");

        Assert.NotNull(descriptor);
        Assert.Equal(SideEffect.ReadOnly, descriptor.SideEffect);
        Assert.Null(gateway.Find("fake_undeclared"));
    }

    [Fact]
    public async Task FR101ToolCallReachesTheServerAndReturnsItsText()
    {
        await using var gateway = await ConnectAsync(FakeServer());

        var output = await gateway.InvokeAsync("fake_mail_list", new Dictionary<string, string> { ["since"] = "today" }, "k", CancellationToken.None);

        Assert.Equal("subject-1 since today", output);
    }

    [Fact]
    public async Task FR104ServerReceivesOnlyItsDeclaredScope()
    {
        await using var gateway = await ConnectAsync(FakeServer());

        var scope = await gateway.InvokeAsync("fake_scope", new Dictionary<string, string>(), "k", CancellationToken.None);

        Assert.Equal("mail.readonly", scope);
    }

    [Fact]
    public async Task FR102HostTokensAreNotPassedToTheServer()
    {
        Environment.SetEnvironmentVariable(HostTokenVariable, "host-secret");
        try
        {
            await using var gateway = await ConnectAsync(FakeServer());

            var visible = await gateway.InvokeAsync("fake_host_token_visible", new Dictionary<string, string>(), "k", CancellationToken.None);

            Assert.Equal("no", visible);
        }
        finally
        {
            Environment.SetEnvironmentVariable(HostTokenVariable, null);
        }
    }

    [Fact]
    public async Task FR105UnreachableServerIsReportedAndOtherServersStillWork()
    {
        var servers = FakeServer().Concat([new McpServerOptions
        {
            Name = "broken",
            Command = "agent-mcp-server-that-does-not-exist",
            Tools = [new McpToolOptions { Name = "mail_list", SideEffect = "ReadOnly" }],
        }]).ToList();

        await using var gateway = await ConnectAsync(servers);

        Assert.Contains(gateway.UnavailableServers, entry => entry.StartsWith("broken:", StringComparison.Ordinal));
        Assert.Null(gateway.Find("broken_mail_list"));
        Assert.NotNull(gateway.Find("fake_mail_list"));
    }

    [Fact]
    public async Task FR97ExternalSendToolRequiresApprovalInAWorkflow()
    {
        var servers = new[]
        {
            new McpServerOptions
            {
                Name = "fake",
                Command = "dotnet",
                Arguments = [FakeServerPath],
                Scopes = "mail.send",
                Tools = [new McpToolOptions { Name = "mail_list", SideEffect = "ExternalSend", RequiredInputs = ["since"] }],
            },
        };
        await using var gateway = await ConnectAsync(servers);
        var plan = new WorkflowPlan([new WorkflowStep("s1", "fake_mail_list", new Dictionary<string, string> { ["since"] = "today" })]);

        Assert.Empty(PlanValidator.Validate(plan, gateway));
        Assert.True(PlanValidator.NeedsApproval(plan, gateway));
    }

    private static List<McpServerOptions> FakeServer() =>
    [
        new McpServerOptions
        {
            Name = "fake",
            Command = "dotnet",
            Arguments = [FakeServerPath],
            Scopes = "mail.readonly",
            Tools =
            [
                new McpToolOptions { Name = "mail_list", SideEffect = "ReadOnly", RequiredInputs = ["since"] },
                new McpToolOptions { Name = "scope", SideEffect = "ReadOnly" },
                new McpToolOptions { Name = "host_token_visible", SideEffect = "ReadOnly" },
                new McpToolOptions { Name = "undeclared_tool_name_not_on_server", SideEffect = "ReadOnly" },
            ],
        },
    ];

    private static Task<McpToolGateway> ConnectAsync(IEnumerable<McpServerOptions> servers) =>
        McpToolGateway.ConnectAsync(servers, CancellationToken.None);
}
