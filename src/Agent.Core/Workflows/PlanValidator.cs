namespace Agent.Core.Workflows;

/// <summary>
/// Checks a plan before anything runs: size, unique IDs, registered tools, required inputs,
/// and references that only point backwards to earlier steps (FR-9.1, FR-9.2, FR-9.3, FR-9.15).
/// </summary>
public static class PlanValidator
{
    public const int MaxSteps = 20;
    public const string ReferencePrefix = "$ref:";

    public static IReadOnlyList<string> Validate(WorkflowPlan plan, IToolCatalog catalog)
    {
        var problems = new List<string>();

        if (plan.Steps.Count == 0)
        {
            problems.Add("The plan has no steps.");
            return problems;
        }

        if (plan.Steps.Count > MaxSteps)
        {
            problems.Add($"The plan has {plan.Steps.Count} steps; the limit is {MaxSteps}.");
        }

        var earlier = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in plan.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id) || earlier.Contains(step.Id))
            {
                problems.Add($"Step id '{step.Id}' is empty or used twice.");
            }

            var descriptor = catalog.Find(step.Tool);
            if (descriptor is null)
            {
                problems.Add($"Step {step.Id} uses '{step.Tool}', which is not a registered tool.");
            }
            else
            {
                foreach (var required in descriptor.RequiredInputs)
                {
                    if (!step.Inputs.ContainsKey(required))
                    {
                        problems.Add($"Step {step.Id} is missing required input '{required}'.");
                    }
                }
            }

            foreach (var value in step.Inputs.Values.Where(value => value.StartsWith(ReferencePrefix, StringComparison.Ordinal)))
            {
                var target = value[ReferencePrefix.Length..];
                if (!earlier.Contains(target))
                {
                    problems.Add($"Step {step.Id} references '{target}', which is not an earlier step.");
                }
            }

            earlier.Add(step.Id);
        }

        return problems;
    }

    /// <summary>True when any step can change something outside the agent. Such plans need approval first (FR-9.7).</summary>
    public static bool NeedsApproval(WorkflowPlan plan, IToolCatalog catalog) =>
        plan.Steps.Any(step => catalog.Find(step.Tool) is { SideEffect: not ToolForge.SideEffect.ReadOnly });
}
