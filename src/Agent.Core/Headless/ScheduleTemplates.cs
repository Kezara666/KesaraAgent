namespace Agent.Core.Headless;

/// <summary>A ready-made schedule a person can adopt without writing a cron expression (FR-9.14).</summary>
public sealed record ScheduleTemplate(string Id, string Name, string Cron, string Prompt);

public static class ScheduleTemplates
{
    public static IReadOnlyList<ScheduleTemplate> All { get; } =
    [
        new("daily-workspace-summary", "Daily workspace summary", "0 9 * * *",
            "Summarise what changed in the workspace since yesterday and list any open approvals."),
        new("hourly-approval-reminder", "Approval reminder", "0 * * * *",
            "List approvals still pending and flag any that expire within six hours."),
        new("weekly-backup-check", "Weekly backup check", "0 3 * * 0",
            "Confirm the most recent backup is recent enough and report its size and age."),
    ];
}
