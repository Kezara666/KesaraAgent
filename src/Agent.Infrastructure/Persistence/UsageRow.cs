namespace Agent.Infrastructure.Persistence;

/// <summary>Token usage for one profile on one UTC day. The composite key is (ProfileId, Day).</summary>
public sealed class UsageRow
{
    public string ProfileId { get; set; } = string.Empty;

    public string Day { get; set; } = string.Empty;

    public long Tokens { get; set; }
}
