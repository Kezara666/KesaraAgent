using System.Text.RegularExpressions;

namespace Agent.Core.ToolForge;

/// <summary>What a tool does to the world. Drives the promotion gate (FR-5.5).</summary>
public enum SideEffect
{
    ReadOnly,
    Write,
    ExternalSend,
}

/// <summary>What the agent wants built. Written by the planner when a capability is missing (FR-5.1).</summary>
public sealed record ToolSpec(
    string Name,
    string Domain,
    string Purpose,
    SideEffect SideEffect,
    bool NeedsNetwork)
{
    private static readonly Regex NamePattern = new("^[a-z][a-z0-9_]{2,40}$", RegexOptions.CultureInvariant);
    private static readonly Regex DomainPattern = new("^[a-z][a-z0-9_-]{1,30}$", RegexOptions.CultureInvariant);

    /// <summary>Names and domains become folder names, so they are restricted to a safe alphabet.</summary>
    public void Validate()
    {
        if (!NamePattern.IsMatch(Name))
        {
            throw new ArgumentException("Tool name must be 3 to 41 lowercase letters, digits, or underscores.", nameof(Name));
        }

        if (!DomainPattern.IsMatch(Domain))
        {
            throw new ArgumentException("Domain must be 2 to 31 lowercase letters, digits, underscores, or hyphens.", nameof(Domain));
        }
    }
}

/// <summary>Generated source and its tests, before any check has run.</summary>
public sealed record GeneratedTool(string Source, string TestSource);

/// <summary>The autonomy tier implied by a side effect. Read-only is the only tier that promotes automatically.</summary>
public static class PromotionTier
{
    public static bool RequiresApproval(SideEffect effect) => effect != SideEffect.ReadOnly;
}
