using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storage.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTagColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Tags",
                type: "TEXT",
                maxLength: 7,
                nullable: false,
                defaultValue: "#37474F");

            // Spread the palette over the tags that already exist, so an upgraded
            // database doesn't come back with every chip the same default grey.
            //
            // The hex values are written out rather than read from TagPalette on
            // purpose: a migration is a record of what happened to a database at a
            // point in time, and it must keep producing that result even after the
            // palette is edited.
            migrationBuilder.Sql("""
                UPDATE Tags SET Color = CASE Id % 10
                    WHEN 0 THEN '#C62828'
                    WHEN 1 THEN '#AD1457'
                    WHEN 2 THEN '#6A1B9A'
                    WHEN 3 THEN '#283593'
                    WHEN 4 THEN '#0277BD'
                    WHEN 5 THEN '#00695C'
                    WHEN 6 THEN '#2E7D32'
                    WHEN 7 THEN '#EF6C00'
                    WHEN 8 THEN '#4E342E'
                    ELSE '#37474F'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "Tags");
        }
    }
}
