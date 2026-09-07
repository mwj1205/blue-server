using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyChangeLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CurrencyChangeLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyType = table.Column<int>(type: "integer", nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false),
                    BalanceBefore = table.Column<int>(type: "integer", nullable: false),
                    BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                    ReasonType = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RewardGrantRecordId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyChangeLogs", x => x.Id);
                    table.CheckConstraint("CK_CurrencyChangeLogs_Balance_Consistent", "\"BalanceAfter\" = \"BalanceBefore\" + \"Delta\"");
                    table.CheckConstraint("CK_CurrencyChangeLogs_BalanceAfter_NonNegative", "\"BalanceAfter\" >= 0");
                    table.CheckConstraint("CK_CurrencyChangeLogs_BalanceBefore_NonNegative", "\"BalanceBefore\" >= 0");
                    table.CheckConstraint("CK_CurrencyChangeLogs_CurrencyType_Valid", "\"CurrencyType\" IN (1, 2)");
                    table.CheckConstraint("CK_CurrencyChangeLogs_Delta_NotZero", "\"Delta\" <> 0");
                    table.CheckConstraint("CK_CurrencyChangeLogs_ReasonType_Valid", "\"ReasonType\" IN (1, 2, 3, 4, 5, 6, 7, 8)");
                    table.CheckConstraint("CK_CurrencyChangeLogs_SourceId_NotEmpty", "length(btrim(\"SourceId\")) > 0");
                    table.ForeignKey(
                        name: "FK_CurrencyChangeLogs_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CurrencyChangeLogs_RewardGrantRecords_RewardGrantRecordId",
                        column: x => x.RewardGrantRecordId,
                        principalTable: "RewardGrantRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyChangeLogs_PlayerId_CreatedAt_Id",
                table: "CurrencyChangeLogs",
                columns: new[] { "PlayerId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyChangeLogs_PlayerId_RequestId_CurrencyType",
                table: "CurrencyChangeLogs",
                columns: new[] { "PlayerId", "RequestId", "CurrencyType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyChangeLogs_RewardGrantRecordId",
                table: "CurrencyChangeLogs",
                column: "RewardGrantRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurrencyChangeLogs");
        }
    }
}
