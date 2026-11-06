namespace Agent.Core.Workflows;

/// <summary>Registry of tools a plan may use. Unregistered tools are never offered to the planner (FR-9.13).</summary>
public interface IToolCatalog
{
    ToolDescriptor? Find(string name);
}

/// <summary>Lists every registered tool, for the tool browser. The planner still sees only what the catalog allows.</summary>
public interface IToolListing
{
    IReadOnlyList<ToolDescriptor> List();
}

/// <summary>Runs one registered tool with resolved inputs. Adapters route to built-in, MCP, or sandboxed tools.</summary>
public interface IToolInvoker
{
    Task<string> InvokeAsync(string tool, IReadOnlyDictionary<string, string> inputs, string idempotencyKey, CancellationToken cancellationToken);
}

/// <summary>Durable run and step state. Survives restarts so runs resume where they stopped.</summary>
public interface IWorkflowStore
{
    Task SaveRunAsync(WorkflowRun run, CancellationToken cancellationToken);

    Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken);

    Task SaveStepAsync(StepRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<StepRecord>> GetStepsAsync(string runId, CancellationToken cancellationToken);
}
