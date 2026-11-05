using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Persistence;

public sealed class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<TaskRow> Tasks => Set<TaskRow>();

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
    }
}
