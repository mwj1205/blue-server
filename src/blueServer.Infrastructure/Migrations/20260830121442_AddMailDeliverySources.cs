using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMailDeliverySources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "Mails",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceType",
                table: "Mails",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // 기존 Mail도 Player별 고유 발송 Key를 갖도록 ID 기반 Source를 Backfill
            migrationBuilder.Sql(
                """
                UPDATE "Mails"
                SET "SourceId" = 'legacy:' || "Id"::text
                WHERE "SourceId" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "SourceId",
                table: "Mails",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mails_PlayerId_SourceType_SourceId",
                table: "Mails",
                columns: new[] { "PlayerId", "SourceType", "SourceId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Mails_SourceId_NotEmpty",
                table: "Mails",
                sql: "length(btrim(\"SourceId\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Mails_SourceType_Valid",
                table: "Mails",
                sql: "\"SourceType\" IN (0, 1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Mails_PlayerId_SourceType_SourceId",
                table: "Mails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Mails_SourceId_NotEmpty",
                table: "Mails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Mails_SourceType_Valid",
                table: "Mails");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "Mails");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "Mails");
        }
    }
}
