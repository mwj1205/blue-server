using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryGrantRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryGrantRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MailId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryGrantRecords", x => x.Id);
                    table.CheckConstraint("CK_InventoryGrantRecords_PayloadHash_Length", "length(\"PayloadHash\") = 64");
                    table.CheckConstraint("CK_InventoryGrantRecords_RequestId_NotEmpty", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_InventoryGrantRecords_Mails_MailId",
                        column: x => x.MailId,
                        principalTable: "Mails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InventoryGrantRecords_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryGrantRecordItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InventoryGrantRecordId = table.Column<long>(type: "bigint", nullable: false),
                    ItemTemplateId = table.Column<int>(type: "integer", nullable: false),
                    RequestedQuantity = table.Column<int>(type: "integer", nullable: false),
                    AppliedQuantity = table.Column<int>(type: "integer", nullable: false),
                    OverflowQuantity = table.Column<int>(type: "integer", nullable: false),
                    QuantityAfter = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryGrantRecordItems", x => x.Id);
                    table.CheckConstraint("CK_InventoryGrantRecordItems_AppliedQuantity_NonNegative", "\"AppliedQuantity\" >= 0");
                    table.CheckConstraint("CK_InventoryGrantRecordItems_OverflowQuantity_NonNegative", "\"OverflowQuantity\" >= 0");
                    table.CheckConstraint("CK_InventoryGrantRecordItems_Quantity_Consistent", "\"RequestedQuantity\" = \"AppliedQuantity\" + \"OverflowQuantity\"");
                    table.CheckConstraint("CK_InventoryGrantRecordItems_QuantityAfter_Range", "\"QuantityAfter\" BETWEEN \"AppliedQuantity\" AND 999999");
                    table.CheckConstraint("CK_InventoryGrantRecordItems_RequestedQuantity_Positive", "\"RequestedQuantity\" > 0");
                    table.ForeignKey(
                        name: "FK_InventoryGrantRecordItems_InventoryGrantRecords",
                        column: x => x.InventoryGrantRecordId,
                        principalTable: "InventoryGrantRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryGrantRecordItems_ItemTemplates_ItemTemplateId",
                        column: x => x.ItemTemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryGrantRecordItems_ItemTemplateId",
                table: "InventoryGrantRecordItems",
                column: "ItemTemplateId");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryGrantRecordItems_RecordId_ItemTemplateId",
                table: "InventoryGrantRecordItems",
                columns: new[] { "InventoryGrantRecordId", "ItemTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryGrantRecords_MailId",
                table: "InventoryGrantRecords",
                column: "MailId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryGrantRecords_PlayerId_RequestId",
                table: "InventoryGrantRecords",
                columns: new[] { "PlayerId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryGrantRecordItems");

            migrationBuilder.DropTable(
                name: "InventoryGrantRecords");
        }
    }
}
