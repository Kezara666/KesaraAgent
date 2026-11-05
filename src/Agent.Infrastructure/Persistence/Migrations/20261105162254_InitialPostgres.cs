using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agent.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialPostgres : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "approvals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Target = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Payload = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ResultSummary = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_approvals", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "audit",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Time = table.Column<long>(type: "bigint", nullable: false),
                Actor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Target = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Result = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "schedules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CronExpression = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Prompt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                LastRunTicks = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedules", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "tasks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Prompt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Result = table.Column<string>(type: "text", nullable: true),
                Error = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tasks", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "usage",
            columns: table => new
            {
                ProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Day = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Tokens = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_usage", x => new { x.ProfileId, x.Day });
            });

        migrationBuilder.CreateIndex(
            name: "IX_approvals_CreatedAt",
            table: "approvals",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_approvals_Status",
            table: "approvals",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_audit_Time",
            table: "audit",
            column: "Time");

        migrationBuilder.CreateIndex(
            name: "IX_tasks_CreatedAt",
            table: "tasks",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_tasks_IdempotencyKey",
            table: "tasks",
            column: "IdempotencyKey",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "approvals");

        migrationBuilder.DropTable(
            name: "audit");

        migrationBuilder.DropTable(
            name: "schedules");

        migrationBuilder.DropTable(
            name: "tasks");

        migrationBuilder.DropTable(
            name: "usage");
    }
}
