using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryItemChangeLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryItemChangeLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    ItemTemplateId = table.Column<int>(type: "integer", nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false),
                    QuantityBefore = table.Column<int>(type: "integer", nullable: false),
                    QuantityAfter = table.Column<int>(type: "integer", nullable: false),
                    ReasonType = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItemChangeLogs", x => x.Id);
                    table.CheckConstraint("CK_InventoryItemChangeLogs_Delta_NotZero", "\"Delta\" <> 0");
                    table.CheckConstraint("CK_InventoryItemChangeLogs_Quantity_Consistent", "\"QuantityAfter\" = \"QuantityBefore\" + \"Delta\"");
                    table.CheckConstraint("CK_InventoryItemChangeLogs_QuantityAfter_Range", "\"QuantityAfter\" BETWEEN 0 AND 999999");
                    table.CheckConstraint("CK_InventoryItemChangeLogs_QuantityBefore_Range", "\"QuantityBefore\" BETWEEN 0 AND 999999");
                    table.CheckConstraint("CK_InventoryItemChangeLogs_ReasonType_Valid", "\"ReasonType\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");
                    table.CheckConstraint("CK_InventoryItemChangeLogs_RequestId_NotEmpty", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_InventoryItemChangeLogs_SourceId_NotEmpty", "length(btrim(\"SourceId\")) > 0");
                    table.ForeignKey(
                        name: "FK_InventoryItemChangeLogs_ItemTemplates_ItemTemplateId",
                        column: x => x.ItemTemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryItemChangeLogs_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItemChangeLogs_ItemTemplateId",
                table: "InventoryItemChangeLogs",
                column: "ItemTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItemChangeLogs_PlayerId_CreatedAt_Id",
                table: "InventoryItemChangeLogs",
                columns: new[] { "PlayerId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItemChangeLogs_PlayerId_RequestId_ItemTemplateId",
                table: "InventoryItemChangeLogs",
                columns: new[] { "PlayerId", "RequestId", "ItemTemplateId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryItemChangeLogs");
        }
    }
}
