using System.Text.Json;
using Agent.Core.Approvals;
using Agent.Core.Audit;
using Agent.Core.Governance;
using Agent.Core.Llm;
using Agent.Core.Ports;
using Agent.Core.Tasks;
using Agent.Core.ToolForge;
using Agent.Core.Workflows;

namespace Agent.Core.Agent;

/// <summary>The outcome of one agent run: the final answer, how many model steps ran, and any approvals still waiting.</summary>
public sealed record AgentRunResult(string Text, int Steps, IReadOnlyList<Guid> PendingApprovals);

/// <summary>
/// The agent loop. The model proposes tool calls, the agent checks each one against the registry, runs read-only tools,
/// and holds anything that changes data or leaves the machine for a person. Results go back to the model until it
/// answers in text, or until the step limit stops the run.
/// </summary>
public sealed class AgentLoop(
    ILlmClient llm,
    IToolCatalog catalog,
    IToolListing listing,
    IToolInvoker invoker,
    IApprovalStore approvals,
    IAuditLog audit,
    TimeProvider clock)
{
    public const int MaxSteps = 6;
    public const string ApprovalKind = "agent.tool";

    private const int MaxToolOutputCharacters = 8000;
    private static readonly TimeSpan ApprovalLifetime = TimeSpan.FromHours(24);

    private const string SystemPrompt =
        "You are a careful assistant with tools. Use a tool when it helps. Read or search files before you describe them. "
        + "Do not invent file contents. A change to a file is only proposed, and it runs after a person approves it. Say so when that applies.";

    public async Task<AgentRunResult> RunAsync(
        string prompt,
        string actor,
        string runId,
        string? traceId,
        CancellationToken cancellationToken,
        IReadOnlyList<ChatTurn>? history = null,
        Guid? conversationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var tools = listing.List()
            .Select(tool => new ModelTool(tool.Name, tool.Description, tool.RequiredInputs))
            .ToList();
        var turns = new List<ChatTurn>
        {
            new("system", SystemPrompt),
        };
        turns.AddRange(history ?? []);
        turns.Add(new ChatTurn("user", prompt));
        var pending = new List<Guid>();

        for (var step = 1; step <= MaxSteps; step++)
        {
            var answer = await llm.ChatAsync(turns, tools, cancellationToken).ConfigureAwait(false);
            if (answer.ToolCalls.Count == 0)
            {
                return new AgentRunResult(answer.Text ?? string.Empty, step, pending);
            }

            turns.Add(new ChatTurn("assistant", answer.Text, answer.ToolCalls));
            foreach (var call in answer.ToolCalls)
            {
                var result = await ExecuteAsync(call, actor, runId, traceId, pending, conversationId, cancellationToken).ConfigureAwait(false);
                turns.Add(new ChatTurn("tool", result, ToolCallId: call.Id));
            }
        }

        return new AgentRunResult(
            "I stopped after the step limit. Ask again with a narrower request.",
            MaxSteps,
            pending);
    }

    private async Task<string> ExecuteAsync(
        ModelToolCall call,
        string actor,
        string runId,
        string? traceId,
        List<Guid> pending,
        Guid? conversationId,
        CancellationToken cancellationToken)
    {
        var descriptor = catalog.Find(call.Name);
        if (descriptor is null)
        {
            await AuditAsync(actor, "agent.tool.refused", call.Name, "Unknown tool", traceId, cancellationToken).ConfigureAwait(false);
            return $"Error: the tool '{call.Name}' does not exist.";
        }

        var missing = descriptor.RequiredInputs.Where(input => !call.Arguments.ContainsKey(input)).ToList();
        if (missing.Count > 0)
        {
            return $"Error: missing inputs for '{call.Name}': {string.Join(", ", missing)}.";
        }

        if (descriptor.SideEffect != SideEffect.ReadOnly)
        {
            // Anything that changes data or leaves the machine waits for a person. The model is told so, and the
            // approval holds the exact call, so the approver sees what would run.
            var now = clock.GetUtcNow();
            var request = new ApprovalRequest(
                Guid.CreateVersion7(),
                ApprovalKind,
                call.Name,
                JsonSerializer.Serialize(new HeldCall(call.Name, call.Arguments, conversationId)),
                ApprovalStatus.Pending,
                now,
                now.Add(ApprovalLifetime),
                DecidedAt: null,
                ResultSummary: null);
            await approvals.AddAsync(request, cancellationToken).ConfigureAwait(false);
            await AuditAsync(actor, "agent.tool.held", call.Name, "Pending", traceId, cancellationToken).ConfigureAwait(false);
            pending.Add(request.Id);
            return $"The tool '{call.Name}' changes data, so it was not run. Approval request {request.Id} is waiting for a person.";
        }

        try
        {
            var output = await invoker.InvokeAsync(call.Name, call.Arguments, $"{runId}:{call.Id}", cancellationToken).ConfigureAwait(false);
            await AuditAsync(actor, "agent.tool.call", call.Name, "Executed", traceId, cancellationToken).ConfigureAwait(false);
            return output.Length <= MaxToolOutputCharacters ? output : output[..MaxToolOutputCharacters] + "\n[truncated]";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Record the exception type only. Messages can carry file contents or secrets.
            await AuditAsync(actor, "agent.tool.call", call.Name, "Failed: " + exception.GetType().Name, traceId, cancellationToken).ConfigureAwait(false);
            return $"Error: the tool failed ({exception.GetType().Name}).";
        }
    }

    private Task AuditAsync(string actor, string action, string target, string result, string? traceId, CancellationToken cancellationToken) =>
        audit.AppendAsync(
            new AuditEntry(Guid.CreateVersion7(), clock.GetUtcNow(), actor, action, target, Tier: null, result, traceId),
            cancellationToken);

    internal sealed record HeldCall(string Name, IReadOnlyDictionary<string, string> Arguments, Guid? ConversationId = null);
}

/// <summary>Decides agent tool calls that were held for approval. An approved call runs once, with the arguments it was held with.</summary>
public sealed class AgentToolActions(
    IApprovalStore approvals,
    IToolInvoker invoker,
    IAuditLog audit,
    TimeProvider clock,
    TaskService? tasks = null)
{
    private const int MaxSummaryCharacters = 256;

    public async Task<ApprovalRequest> DecideAsync(Guid approvalId, bool approve, string approver, CancellationToken cancellationToken)
    {
        var request = await approvals.GetAsync(approvalId, cancellationToken).ConfigureAwait(false)
            ?? throw new ApprovalNotFoundException(approvalId);

        if (request.Kind != AgentLoop.ApprovalKind || request.Status != ApprovalStatus.Pending)
        {
            throw new ApprovalStateException("The approval is not a pending agent tool call.");
        }

        var now = clock.GetUtcNow();
        if (now > request.ExpiresAt)
        {
            await approvals.UpdateAsync(request with { Status = ApprovalStatus.Expired, DecidedAt = now }, cancellationToken)
                .ConfigureAwait(false);
            throw new ApprovalStateException("The approval has expired.");
        }

        if (!approve)
        {
            var rejected = request with { Status = ApprovalStatus.Rejected, DecidedAt = now };
            await approvals.UpdateAsync(rejected, cancellationToken).ConfigureAwait(false);
            await AuditAsync(approver, "approval.rejected", request.Target, "Rejected", cancellationToken).ConfigureAwait(false);
            return rejected;
        }

        var approved = request with { Status = ApprovalStatus.Approved, DecidedAt = now };
        await approvals.UpdateAsync(approved, cancellationToken).ConfigureAwait(false);
        await AuditAsync(approver, "approval.approved", request.Target, "Approved", cancellationToken).ConfigureAwait(false);

        var held = JsonSerializer.Deserialize<AgentLoop.HeldCall>(request.Payload)
            ?? throw new InvalidOperationException("The held tool call could not be read.");

        string summary;
        string output;
        ApprovalStatus status;
        try
        {
            output = await invoker.InvokeAsync(held.Name, held.Arguments, $"approval:{approvalId}", cancellationToken).ConfigureAwait(false);
            status = ApprovalStatus.Executed;
            summary = "Executed";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            output = string.Empty;
            status = ApprovalStatus.Failed;
            summary = "Failed: " + exception.GetType().Name;
        }

        var finished = approved with { Status = status, DecidedAt = now, ResultSummary = Truncate(summary) };
        await approvals.UpdateAsync(finished, cancellationToken).ConfigureAwait(false);
        await AuditAsync(approver, "agent.tool.executed", request.Target, finished.ResultSummary!, cancellationToken).ConfigureAwait(false);

        // The conversation continues with the result, so the person does not have to restate the request.
        if (held.ConversationId is { } conversationId && tasks is not null)
        {
            await tasks.SubmitAsync(
                    ContinuationPrompt(held.Name, status, output),
                    "approval-" + approvalId.ToString("N"),
                    cancellationToken,
                    conversationId)
                .ConfigureAwait(false);
        }

        return finished;
    }

    private static string Truncate(string text) => text.Length <= MaxSummaryCharacters ? text : text[..MaxSummaryCharacters];

    private static string ContinuationPrompt(string tool, ApprovalStatus status, string output) =>
        status == ApprovalStatus.Executed
            ? $"The change you proposed with the tool '{tool}' was approved and applied. Its result: {Shorten(output, 1500)}. Briefly tell the person what was done, and then finish the request."
            : $"The change you proposed with the tool '{tool}' was approved but failed. Tell the person, and suggest what to do next.";

    private static string Shorten(string text, int limit) => text.Length <= limit ? text : text[..limit] + "...";

    private Task AuditAsync(string actor, string action, string target, string result, CancellationToken cancellationToken) =>
        audit.AppendAsync(
            new AuditEntry(Guid.CreateVersion7(), clock.GetUtcNow(), actor, action, target, Tier: null, result, null),
            cancellationToken);
}
