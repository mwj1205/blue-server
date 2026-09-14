using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace blueServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseCatalogItemTemplateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "ItemTemplates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionKey",
                table: "ItemTemplates",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ItemTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "ItemTemplates",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "ItemTemplates" AS item
                SET
                    "Code" = catalog."Code",
                    "NameKey" = catalog."NameKey",
                    "DescriptionKey" = catalog."DescriptionKey"
                FROM (
                    VALUES
                        (1001, 'growth_material_basic', 'item.growth_material.basic.name', 'item.growth_material.basic.description'),
                        (2001, 'test_character_shard', 'item.test_character_shard.name', 'item.test_character_shard.description'),
                        (3001, 'recovery_potion_small', 'item.recovery_potion.small.name', 'item.recovery_potion.small.description'),
                        (4001, 'test_event_token', 'item.test_event_token.name', 'item.test_event_token.description')
                ) AS catalog("Id", "Code", "NameKey", "DescriptionKey")
                WHERE item."Id" = catalog."Id";

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "ItemTemplates"
                        WHERE "Code" IS NULL
                           OR "NameKey" IS NULL
                           OR "DescriptionKey" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'ItemTemplates contains IDs that are absent from item-templates.v1.json.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "ItemTemplates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DescriptionKey",
                table: "ItemTemplates",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NameKey",
                table: "ItemTemplates",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemTemplates_Name_NotEmpty",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "ItemTemplates");

            migrationBuilder.CreateIndex(
                name: "IX_ItemTemplates_Code",
                table: "ItemTemplates",
                column: "Code",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemTemplates_Code_NotEmpty",
                table: "ItemTemplates",
                sql: "length(btrim(\"Code\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemTemplates_DescriptionKey_NotEmpty",
                table: "ItemTemplates",
                sql: "length(btrim(\"DescriptionKey\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemTemplates_NameKey_NotEmpty",
                table: "ItemTemplates",
                sql: "length(btrim(\"NameKey\")) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ItemTemplates_Code",
                table: "ItemTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemTemplates_Code_NotEmpty",
                table: "ItemTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemTemplates_DescriptionKey_NotEmpty",
                table: "ItemTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemTemplates_NameKey_NotEmpty",
                table: "ItemTemplates");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ItemTemplates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "ItemTemplates"
                SET "Name" = "Code";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ItemTemplates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemTemplates_Name_NotEmpty",
                table: "ItemTemplates",
                sql: "length(btrim(\"Name\")) > 0");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "DescriptionKey",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "ItemTemplates");
        }
    }
}
