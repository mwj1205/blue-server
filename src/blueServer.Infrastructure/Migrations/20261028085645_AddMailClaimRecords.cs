using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMailClaimRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MailClaimRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    MailId = table.Column<long>(type: "bigint", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MailClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantedGold = table.Column<int>(type: "integer", nullable: false),
                    GrantedGem = table.Column<int>(type: "integer", nullable: false),
                    CurrentGold = table.Column<int>(type: "integer", nullable: false),
                    CurrentGem = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailClaimRecords", x => x.Id);
                    table.CheckConstraint("CK_MailClaimRecords_Gem_Range", "\"GrantedGem\" >= 0 AND \"CurrentGem\" >= \"GrantedGem\"");
                    table.CheckConstraint("CK_MailClaimRecords_Gold_Range", "\"GrantedGold\" >= 0 AND \"CurrentGold\" >= \"GrantedGold\"");
                    table.CheckConstraint("CK_MailClaimRecords_MailClaimedAt_Valid", "\"MailClaimedAt\" IS NULL OR \"MailClaimedAt\" <= \"ProcessedAt\"");
                    table.CheckConstraint("CK_MailClaimRecords_RequestId_NotEmpty", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_MailClaimRecords_Mails_MailId",
                        column: x => x.MailId,
                        principalTable: "Mails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MailClaimRecords_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MailClaimRecordItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MailClaimRecordId = table.Column<long>(type: "bigint", nullable: false),
                    ItemTemplateId = table.Column<int>(type: "integer", nullable: false),
                    GrantedQuantity = table.Column<int>(type: "integer", nullable: false),
                    RemainingQuantity = table.Column<int>(type: "integer", nullable: false),
                    QuantityAfter = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailClaimRecordItems", x => x.Id);
                    table.CheckConstraint("CK_MailClaimRecordItems_GrantedQuantity_NonNegative", "\"GrantedQuantity\" >= 0");
                    table.CheckConstraint("CK_MailClaimRecordItems_QuantityAfter_Range", "\"QuantityAfter\" BETWEEN \"GrantedQuantity\" AND 999999");
                    table.CheckConstraint("CK_MailClaimRecordItems_RemainingQuantity_NonNegative", "\"RemainingQuantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_MailClaimRecordItems_ItemTemplates_ItemTemplateId",
                        column: x => x.ItemTemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MailClaimRecordItems_MailClaimRecords_MailClaimRecordId",
                        column: x => x.MailClaimRecordId,
                        principalTable: "MailClaimRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailClaimRecordItems_ItemTemplateId",
                table: "MailClaimRecordItems",
                column: "ItemTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MailClaimRecordItems_MailClaimRecordId_ItemTemplateId",
                table: "MailClaimRecordItems",
                columns: new[] { "MailClaimRecordId", "ItemTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailClaimRecords_MailId",
                table: "MailClaimRecords",
                column: "MailId");

            migrationBuilder.CreateIndex(
                name: "IX_MailClaimRecords_PlayerId_RequestId",
                table: "MailClaimRecords",
                columns: new[] { "PlayerId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailClaimRecordItems");

            migrationBuilder.DropTable(
                name: "MailClaimRecords");
        }
    }
}
