using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Persistence;

public sealed class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<TaskRow> Tasks => Set<TaskRow>();

    public DbSet<UsageRow> Usage => Set<UsageRow>();

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
    }
}
