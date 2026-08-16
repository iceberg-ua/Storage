using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storage.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTags : Migration
    {
        // SQLite has no string-split function, so walking a comma-separated value takes
        // a recursive CTE: seed with the whole string plus a trailing comma, then peel
        // one field off the front per iteration until nothing is left.
        private const string SplitCte = @"
WITH RECURSIVE Split(ItemId, Tag, Rest) AS (
    SELECT Id, NULL, Tags || ','
    FROM Items
    WHERE Tags IS NOT NULL AND TRIM(Tags) <> ''
    UNION ALL
    SELECT ItemId,
           TRIM(SUBSTR(Rest, 1, INSTR(Rest, ',') - 1)),
           SUBSTR(Rest, INSTR(Rest, ',') + 1)
    FROM Split
    WHERE Rest <> ''
)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Order matters: the tables and the unique index have to exist, and the old
            // Items.Tags column has to still be there, when the data migration runs.
            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateTable(
                name: "ItemTag",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTag", x => new { x.ItemId, x.TagId });
                    table.ForeignKey(
                        name: "FK_ItemTag_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemTag_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemTag_TagId",
                table: "ItemTag",
                column: "TagId");

            // DISTINCT is case-sensitive, so "Tools" and "tools" both reach the insert.
            // The unique NOCASE index rejects the second one and the casing that came
            // first wins — which is the de-duplication rule the app uses from here on.
            migrationBuilder.Sql(SplitCte + @"
INSERT OR IGNORE INTO Tags (Name)
SELECT DISTINCT Tag
FROM Split
WHERE Tag IS NOT NULL AND Tag <> '';");

            // Name's NOCASE collation governs this join, so every case-variant of a tag
            // resolves to the single row inserted above.
            migrationBuilder.Sql(SplitCte + @"
INSERT OR IGNORE INTO ItemTag (ItemId, TagId)
SELECT DISTINCT s.ItemId, t.Id
FROM Split s
JOIN Tags t ON t.Name = s.Tag
WHERE s.Tag IS NOT NULL AND s.Tag <> '';");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Items",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            // Fold the assignments back into the comma-separated form the column held.
            migrationBuilder.Sql(@"
UPDATE Items
SET Tags = (
    SELECT group_concat(t.Name, ', ')
    FROM ItemTag it
    JOIN Tags t ON t.Id = it.TagId
    WHERE it.ItemId = Items.Id
)
WHERE EXISTS (SELECT 1 FROM ItemTag it WHERE it.ItemId = Items.Id);");

            migrationBuilder.DropTable(
                name: "ItemTag");

            migrationBuilder.DropTable(
                name: "Tags");
        }
    }
}
