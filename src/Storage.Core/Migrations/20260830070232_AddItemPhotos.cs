using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storage.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddItemPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Order matters: the table has to exist, and Items.PhotoPath has to still
            // be there, when the data migration between them runs.
            migrationBuilder.CreateTable(
                name: "ItemPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemPhotos_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemPhotos_ItemId_SortOrder",
                table: "ItemPhotos",
                columns: new[] { "ItemId", "SortOrder" });

            // Every existing photo becomes that item's primary. TRIM guards the same
            // whitespace-only case the tag migration had to: a blank PhotoPath is "no
            // photo", and FileName is NOT NULL, so it must not become a row.
            migrationBuilder.Sql(@"
INSERT INTO ItemPhotos (ItemId, FileName, SortOrder)
SELECT Id, TRIM(PhotoPath), 0
FROM Items
WHERE PhotoPath IS NOT NULL AND TRIM(PhotoPath) <> '';");

            migrationBuilder.DropColumn(
                name: "PhotoPath",
                table: "Items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoPath",
                table: "Items",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            // Only the primary fits back into a single column. The rest stay on disk
            // and become orphans that the downgraded app's sweep will collect —
            // nothing is deleted here, so a re-upgrade before that sweep runs is a
            // lossy but recoverable trip.
            migrationBuilder.Sql(@"
UPDATE Items
SET PhotoPath = (
    SELECT p.FileName
    FROM ItemPhotos p
    WHERE p.ItemId = Items.Id
    ORDER BY p.SortOrder, p.Id
    LIMIT 1
);");

            migrationBuilder.DropTable(
                name: "ItemPhotos");
        }
    }
}
