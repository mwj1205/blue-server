using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMailItemClaimedQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClaimedQuantity",
                table: "MailItemAttachments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MailItemAttachments_ClaimedQuantity_Range",
                table: "MailItemAttachments",
                sql: "\"ClaimedQuantity\" BETWEEN 0 AND \"Quantity\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MailItemAttachments_ClaimedQuantity_Range",
                table: "MailItemAttachments");

            migrationBuilder.DropColumn(
                name: "ClaimedQuantity",
                table: "MailItemAttachments");
        }
    }
}
