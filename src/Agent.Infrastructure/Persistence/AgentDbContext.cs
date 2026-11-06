using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Agent.Infrastructure.Persistence;

public sealed class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    /// <summary>
    /// SQLite cannot ORDER BY a DateTimeOffset column. Timestamps used for ordering are stored as UTC ticks,
    /// which sort correctly and round-trip exactly.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicks =
        new(value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    public DbSet<TaskRow> Tasks => Set<TaskRow>();

    public DbSet<UsageRow> Usage => Set<UsageRow>();

    public DbSet<AuditRow> Audit => Set<AuditRow>();

    public DbSet<ApprovalRow> Approvals => Set<ApprovalRow>();

    public DbSet<ScheduleRow> Schedules => Set<ScheduleRow>();

    public DbSet<ApiTokenRow> ApiTokens => Set<ApiTokenRow>();

    public DbSet<WorkflowRunRow> WorkflowRuns => Set<WorkflowRunRow>();

    public DbSet<WorkflowStepRow> WorkflowSteps => Set<WorkflowStepRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskRow>(entity =>
        {
            entity.ToTable("tasks");
            entity.HasKey(task => task.Id);
            entity.Property(task => task.Prompt).IsRequired().HasMaxLength(4000);
            entity.Property(task => task.Status).IsRequired().HasMaxLength(32);
            entity.Property(task => task.IdempotencyKey).HasMaxLength(128);
            entity.Property(task => task.Error).HasMaxLength(256);
            entity.Property(task => task.CreatedAt).HasConversion(UtcTicks);
            entity.Property(task => task.UpdatedAt).HasConversion(UtcTicks);
            entity.HasIndex(task => task.IdempotencyKey).IsUnique();
            entity.HasIndex(task => task.CreatedAt);
        });

        modelBuilder.Entity<UsageRow>(entity =>
        {
            entity.ToTable("usage");
            entity.HasKey(usage => new { usage.ProfileId, usage.Day });
            entity.Property(usage => usage.ProfileId).HasMaxLength(64);
            entity.Property(usage => usage.Day).HasMaxLength(10);
        });

        modelBuilder.Entity<AuditRow>(entity =>
        {
            entity.ToTable("audit");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.Time).HasConversion(UtcTicks);
            entity.Property(audit => audit.Actor).IsRequired().HasMaxLength(64);
            entity.Property(audit => audit.Action).IsRequired().HasMaxLength(64);
            entity.Property(audit => audit.Target).IsRequired().HasMaxLength(256);
            entity.Property(audit => audit.Tier).HasMaxLength(32);
            entity.Property(audit => audit.Result).IsRequired().HasMaxLength(256);
            entity.Property(audit => audit.TraceId).HasMaxLength(64);
            entity.HasIndex(audit => audit.Time);
        });

        modelBuilder.Entity<ApprovalRow>(entity =>
        {
            entity.ToTable("approvals");
            entity.HasKey(approval => approval.Id);
            entity.Property(approval => approval.Kind).IsRequired().HasMaxLength(32);
            entity.Property(approval => approval.Target).IsRequired().HasMaxLength(256);
            entity.Property(approval => approval.Payload).IsRequired();
            entity.Property(approval => approval.Status).IsRequired().HasMaxLength(32);
            entity.Property(approval => approval.CreatedAt).HasConversion(UtcTicks);
            entity.Property(approval => approval.ExpiresAt).HasConversion(UtcTicks);
            entity.Property(approval => approval.ResultSummary).HasMaxLength(256);
            entity.HasIndex(approval => approval.Status);
            entity.HasIndex(approval => approval.CreatedAt);
        });

        modelBuilder.Entity<ScheduleRow>(entity =>
        {
            entity.ToTable("schedules");
            entity.HasKey(schedule => schedule.Id);
            entity.Property(schedule => schedule.Name).IsRequired().HasMaxLength(64);
            entity.Property(schedule => schedule.CronExpression).IsRequired().HasMaxLength(64);
            entity.Property(schedule => schedule.Prompt).IsRequired().HasMaxLength(4000);
            entity.Property(schedule => schedule.LastRunTicks);
        });

        modelBuilder.Entity<ApiTokenRow>(entity =>
        {
            entity.ToTable("api_tokens");
            entity.HasKey(token => token.Id);
            entity.Property(token => token.Name).IsRequired().HasMaxLength(64);
            entity.Property(token => token.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(token => token.Scopes).IsRequired().HasMaxLength(64);
            entity.Property(token => token.CreatedAt).HasConversion(UtcTicks);
            entity.HasIndex(token => token.TokenHash).IsUnique();
        });

        modelBuilder.Entity<WorkflowRunRow>(entity =>
        {
            entity.ToTable("workflow_runs");
            entity.HasKey(run => run.RunId);
            entity.Property(run => run.RunId).HasMaxLength(64);
            entity.Property(run => run.PlanJson).IsRequired();
            entity.Property(run => run.Status).IsRequired().HasMaxLength(32);
            entity.Property(run => run.TraceId).HasMaxLength(64);
            entity.Property(run => run.FailedStep).HasMaxLength(64);
        });

        modelBuilder.Entity<WorkflowStepRow>(entity =>
        {
            entity.ToTable("workflow_steps");
            entity.HasKey(step => new { step.RunId, step.StepId });
            entity.Property(step => step.RunId).HasMaxLength(64);
            entity.Property(step => step.StepId).HasMaxLength(64);
            entity.Property(step => step.Status).IsRequired().HasMaxLength(32);
            entity.Property(step => step.Output).IsRequired().HasMaxLength(16384);
            entity.Property(step => step.IdempotencyKey).HasMaxLength(128);
        });
    }
}
