using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agent.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddApiTokens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "api_tokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Scopes = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_api_tokens", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_api_tokens_TokenHash",
            table: "api_tokens",
            column: "TokenHash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "api_tokens");
    }
}
