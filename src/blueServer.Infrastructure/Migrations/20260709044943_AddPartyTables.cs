using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Parties",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    PartyNo = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Parties_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartySlots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    SlotIndex = table.Column<int>(type: "integer", nullable: false),
                    OwnedCharacterId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartySlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartySlots_OwnedCharacters_OwnedCharacterId",
                        column: x => x.OwnedCharacterId,
                        principalTable: "OwnedCharacters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartySlots_Parties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Parties_PlayerId_PartyNo",
                table: "Parties",
                columns: new[] { "PlayerId", "PartyNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartySlots_OwnedCharacterId",
                table: "PartySlots",
                column: "OwnedCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_PartySlots_PartyId_OwnedCharacterId",
                table: "PartySlots",
                columns: new[] { "PartyId", "OwnedCharacterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartySlots_PartyId_SlotIndex",
                table: "PartySlots",
                columns: new[] { "PartyId", "SlotIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartySlots");

            migrationBuilder.DropTable(
                name: "Parties");
        }
    }
}
