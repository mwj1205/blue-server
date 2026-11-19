using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyBoxUseRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryItemChangeLogs_ReasonType_Valid",
                table: "InventoryItemChangeLogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CurrencyChangeLogs_ReasonType_Valid",
                table: "CurrencyChangeLogs");

            migrationBuilder.CreateTable(
                name: "CurrencyBoxUseRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RewardRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemTemplateId = table.Column<int>(type: "integer", nullable: false),
                    UsedQuantity = table.Column<int>(type: "integer", nullable: false),
                    QuantityAfter = table.Column<int>(type: "integer", nullable: false),
                    CurrencyType = table.Column<int>(type: "integer", nullable: false),
                    GrantedAmount = table.Column<int>(type: "integer", nullable: false),
                    CurrentGold = table.Column<int>(type: "integer", nullable: false),
                    CurrentGem = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyBoxUseRecords", x => x.Id);
                    table.CheckConstraint("CK_CurrencyBoxUseRecords_CurrencyType_Valid", "\"CurrencyType\" IN (1, 2)");
                    table.CheckConstraint("CK_CurrencyBoxUseRecords_CurrentBalances_NonNegative", "\"CurrentGold\" >= 0 AND \"CurrentGem\" >= 0");
                    table.CheckConstraint("CK_CurrencyBoxUseRecords_GrantedAmount_Positive", "\"GrantedAmount\" > 0");
                    table.CheckConstraint("CK_CurrencyBoxUseRecords_QuantityAfter_Range", "\"QuantityAfter\" BETWEEN 0 AND 999999");
                    table.CheckConstraint("CK_CurrencyBoxUseRecords_QuantityBefore_Range", "\"UsedQuantity\"::bigint + \"QuantityAfter\" <= 999999");
                    table.CheckConstraint("CK_CurrencyBoxUseRecords_RequestId_NotEmpty", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_CurrencyBoxUseRecords_RewardBalance_Consistent", "(\"CurrencyType\" = 1 AND \"CurrentGold\" >= \"GrantedAmount\") OR (\"CurrencyType\" = 2 AND \"CurrentGem\" >= \"GrantedAmount\")");
                    table.CheckConstraint("CK_CurrencyBoxUseRecords_RewardRequestId_NotEmpty", "\"RewardRequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_CurrencyBoxUseRecords_UsedQuantity_Range", "\"UsedQuantity\" BETWEEN 1 AND 999999");
                    table.ForeignKey(
                        name: "FK_CurrencyBoxUseRecords_ItemTemplates_ItemTemplateId",
                        column: x => x.ItemTemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CurrencyBoxUseRecords_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryItemChangeLogs_ReasonType_Valid",
                table: "InventoryItemChangeLogs",
                sql: "\"ReasonType\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CurrencyChangeLogs_ReasonType_Valid",
                table: "CurrencyChangeLogs",
                sql: "\"ReasonType\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9)");

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyBoxUseRecords_ItemTemplateId",
                table: "CurrencyBoxUseRecords",
                column: "ItemTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyBoxUseRecords_PlayerId_ProcessedAt_Id",
                table: "CurrencyBoxUseRecords",
                columns: new[] { "PlayerId", "ProcessedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyBoxUseRecords_PlayerId_RequestId",
                table: "CurrencyBoxUseRecords",
                columns: new[] { "PlayerId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyBoxUseRecords_PlayerId_RewardRequestId",
                table: "CurrencyBoxUseRecords",
                columns: new[] { "PlayerId", "RewardRequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurrencyBoxUseRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryItemChangeLogs_ReasonType_Valid",
                table: "InventoryItemChangeLogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CurrencyChangeLogs_ReasonType_Valid",
                table: "CurrencyChangeLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryItemChangeLogs_ReasonType_Valid",
                table: "InventoryItemChangeLogs",
                sql: "\"ReasonType\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CurrencyChangeLogs_ReasonType_Valid",
                table: "CurrencyChangeLogs",
                sql: "\"ReasonType\" IN (1, 2, 3, 4, 5, 6, 7, 8)");
        }
    }
}
