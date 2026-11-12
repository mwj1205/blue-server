using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMailClaimAllRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MailClaimAllRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentGold = table.Column<int>(type: "integer", nullable: false),
                    CurrentGem = table.Column<int>(type: "integer", nullable: false),
                    HasMore = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailClaimAllRecords", x => x.Id);
                    table.CheckConstraint("CK_MailClaimAllRecords_CurrentGem_NonNegative", "\"CurrentGem\" >= 0");
                    table.CheckConstraint("CK_MailClaimAllRecords_CurrentGold_NonNegative", "\"CurrentGold\" >= 0");
                    table.CheckConstraint("CK_MailClaimAllRecords_RequestId_NotEmpty", "\"RequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_MailClaimAllRecords_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MailClaimAllRecordEntries",
                columns: table => new
                {
                    MailClaimAllRecordId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    MailClaimRecordId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailClaimAllRecordEntries", x => new { x.MailClaimAllRecordId, x.Position });
                    table.CheckConstraint("CK_MailClaimAllRecordEntries_Position_Range", "\"Position\" >= 0 AND \"Position\" < 100");
                    table.ForeignKey(
                        name: "FK_MailClaimAllRecordEntries_MailClaimAllRecords_MailClaimAllR~",
                        column: x => x.MailClaimAllRecordId,
                        principalTable: "MailClaimAllRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MailClaimAllRecordEntries_MailClaimRecords_MailClaimRecordId",
                        column: x => x.MailClaimRecordId,
                        principalTable: "MailClaimRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailClaimAllRecordEntries_MailClaimRecordId",
                table: "MailClaimAllRecordEntries",
                column: "MailClaimRecordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailClaimAllRecords_PlayerId_RequestId",
                table: "MailClaimAllRecords",
                columns: new[] { "PlayerId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailClaimAllRecordEntries");

            migrationBuilder.DropTable(
                name: "MailClaimAllRecords");
        }
    }
}
