using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agent.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddWorkflows : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "workflow_runs",
            columns: table => new
            {
                RunId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PlanJson = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                FailedStep = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workflow_runs", x => x.RunId);
            });

        migrationBuilder.CreateTable(
            name: "workflow_steps",
            columns: table => new
            {
                RunId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                StepId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Output = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Attempts = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workflow_steps", x => new { x.RunId, x.StepId });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "workflow_runs");

        migrationBuilder.DropTable(
            name: "workflow_steps");
    }
}
