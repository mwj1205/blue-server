using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TestMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Start",
                table: "OwnedCharacters",
                newName: "Star");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Star",
                table: "OwnedCharacters",
                newName: "Start");
        }
    }
}
