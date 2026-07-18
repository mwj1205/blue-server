using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStageClearTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StageTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    RewardGold = table.Column<int>(type: "integer", nullable: false),
                    RewardGem = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StageClearRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    StageTemplateId = table.Column<int>(type: "integer", nullable: false),
                    ClearCount = table.Column<int>(type: "integer", nullable: false),
                    FirstClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageClearRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageClearRecords_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StageClearRecords_StageTemplates_StageTemplateId",
                        column: x => x.StageTemplateId,
                        principalTable: "StageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "StageTemplates",
                columns: new[] { "Id", "Name", "RewardGem", "RewardGold" },
                values: new object[] { 1, "1-1", 10, 100 });

            migrationBuilder.CreateIndex(
                name: "IX_StageClearRecords_PlayerId_StageTemplateId",
                table: "StageClearRecords",
                columns: new[] { "PlayerId", "StageTemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageClearRecords_StageTemplateId",
                table: "StageClearRecords",
                column: "StageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_StageTemplates_Name",
                table: "StageTemplates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StageClearRecords");

            migrationBuilder.DropTable(
                name: "StageTemplates");
        }
    }
}
