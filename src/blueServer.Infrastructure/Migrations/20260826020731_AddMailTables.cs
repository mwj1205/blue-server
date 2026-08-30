using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMailTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Mails",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mails", x => x.Id);
                    table.CheckConstraint("CK_Mails_ClaimedAt_After_SentAt", "\"ClaimedAt\" IS NULL OR \"ClaimedAt\" >= \"SentAt\"");
                    table.CheckConstraint("CK_Mails_ExpiresAt_After_SentAt", "\"ExpiresAt\" IS NULL OR \"ExpiresAt\" > \"SentAt\"");
                    table.CheckConstraint("CK_Mails_ReadAt_After_SentAt", "\"ReadAt\" IS NULL OR \"ReadAt\" >= \"SentAt\"");
                    table.ForeignKey(
                        name: "FK_Mails_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MailAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MailId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailAttachments", x => x.Id);
                    table.CheckConstraint("CK_MailAttachments_Amount_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_MailAttachments_Mails_MailId",
                        column: x => x.MailId,
                        principalTable: "Mails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailAttachments_MailId_Type",
                table: "MailAttachments",
                columns: new[] { "MailId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mails_PlayerId_SentAt",
                table: "Mails",
                columns: new[] { "PlayerId", "SentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailAttachments");

            migrationBuilder.DropTable(
                name: "Mails");
        }
    }
}
