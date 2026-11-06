using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agent.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDevices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "devices",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Endpoint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                CapabilitiesJson = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_devices", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "devices");
    }
}
