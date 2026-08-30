using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Storage.Core.Data;
using Storage.Core.Models;

namespace Storage.Tests;

// The other test fixtures build their schema with EnsureCreated(), which reads the
// model and never runs a line of migration SQL. The data migrations — the parts that
// must not lose anyone's tags or photos — can only be checked by actually migrating.
public class MigrationTests : IDisposable
{
    // The schema as it stood before AddTags: Items still has its free-text Tags column.
    private const string BeforeTags = "20260809162515_AddItemQuantity";

    // Tags exist as entities but have no colour yet.
    private const string BeforeColor = "20260816170538_AddTags";

    // The last schema with one photo per item, held in Items.PhotoPath.
    private const string BeforePhotos = "20260816180839_AddTagColor";

    private readonly SqliteConnection _connection;
    private readonly StorageDbContext _context;

    public MigrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<StorageDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new StorageDbContext(options);
    }

    [Fact]
    public async Task AddTagsSplitsLegacyCommaSeparatedTags()
    {
        var migrator = _context.GetService<IMigrator>();
        migrator.Migrate(BeforeTags);

        _context.Database.ExecuteSqlRaw(
            """
            INSERT INTO Items (Name, Tags, Quantity, CreatedAt) VALUES
                ('Cordless Drill', 'tools, power, Tools', 1, datetime('now')),
                ('Circular Saw',   'TOOLS,power',         1, datetime('now')),
                ('Winter Coat',    NULL,                  1, datetime('now')),
                ('Spare Bulbs',    '   ',                 1, datetime('now'));
            """);

        migrator.Migrate();

        // Case-variants collapse to one row each, under the casing seen first.
        var tags = await _context.Tags.OrderBy(t => t.Name).ToListAsync();
        Assert.Equal(["power", "tools"], tags.Select(t => t.Name));

        var items = await _context.Items
            .Include(i => i.Tags)
            .OrderBy(i => i.Name)
            .ToListAsync();

        var saw = items.Single(i => i.Name == "Circular Saw");
        Assert.Equal(["power", "tools"], saw.Tags.Select(t => t.Name).Order());

        var drill = items.Single(i => i.Name == "Cordless Drill");
        Assert.Equal(["power", "tools"], drill.Tags.Select(t => t.Name).Order());

        // Both items point at the same rows rather than at copies of their own.
        Assert.Equal(
            drill.Tags.Select(t => t.Id).Order(),
            saw.Tags.Select(t => t.Id).Order());

        Assert.Empty(items.Single(i => i.Name == "Winter Coat").Tags);
        Assert.Empty(items.Single(i => i.Name == "Spare Bulbs").Tags);
    }

    [Fact]
    public async Task AddTagColorBackfillsExistingTags()
    {
        var migrator = _context.GetService<IMigrator>();
        migrator.Migrate(BeforeColor);

        _context.Database.ExecuteSqlRaw(
            "INSERT INTO Tags (Name) VALUES ('tools'), ('power'), ('books'), ('winter');");

        migrator.Migrate();

        var colors = await _context.Tags.Select(t => t.Color).ToListAsync();

        Assert.Equal(4, colors.Count);
        Assert.All(colors, c => Assert.Contains(c, TagPalette.Colors));

        // Spread across the palette rather than every tag landing on the default.
        Assert.True(colors.Distinct().Count() > 1);
    }

    [Fact]
    public void AddTagsDropsTheOldTagsColumn()
    {
        _context.Database.Migrate();

        Assert.DoesNotContain("Tags", GetColumns("Items"));
    }

    [Fact]
    public void AddTagsDownRestoresTheOldColumnWithItsValues()
    {
        var migrator = _context.GetService<IMigrator>();
        migrator.Migrate(BeforeTags);

        _context.Database.ExecuteSqlRaw(
            "INSERT INTO Items (Name, Tags, Quantity, CreatedAt) VALUES ('Drill', 'tools, power', 1, datetime('now'));");

        migrator.Migrate();
        migrator.Migrate(BeforeTags);

        Assert.Contains("Tags", GetColumns("Items"));

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Tags FROM Items WHERE Name = 'Drill'";
        var restored = (string?)command.ExecuteScalar();

        Assert.NotNull(restored);
        Assert.Equal(["power", "tools"], restored!.Split(", ").Order());
    }

    [Fact]
    public async Task AddItemPhotosMovesEveryPhotoIntoItsOwnRow()
    {
        var migrator = _context.GetService<IMigrator>();
        migrator.Migrate(BeforePhotos);

        _context.Database.ExecuteSqlRaw(
            """
            INSERT INTO Items (Name, PhotoPath, Quantity, CreatedAt) VALUES
                ('Cordless Drill', 'drill.jpg', 1, datetime('now')),
                ('Circular Saw',   'saw.jpg',   1, datetime('now')),
                ('Winter Coat',    NULL,        1, datetime('now')),
                ('Spare Bulbs',    '   ',       1, datetime('now'));
            """);

        migrator.Migrate();

        var items = await _context.Items
            .Include(i => i.Photos)
            .OrderBy(i => i.Name)
            .ToListAsync();

        // The photo an item had is still its photo, and is now the primary.
        var drill = items.Single(i => i.Name == "Cordless Drill");
        var photo = Assert.Single(drill.Photos);
        Assert.Equal("drill.jpg", photo.FileName);
        Assert.Equal(0, photo.SortOrder);
        Assert.Equal("drill.jpg", drill.PrimaryPhotoFileName);

        Assert.Equal("saw.jpg", items.Single(i => i.Name == "Circular Saw").PrimaryPhotoFileName);

        // Nothing was invented for the items that had no photo. A whitespace-only
        // path is one of those: FileName is NOT NULL, and a row naming no file would
        // show as a broken thumbnail forever.
        Assert.Empty(items.Single(i => i.Name == "Winter Coat").Photos);
        Assert.Empty(items.Single(i => i.Name == "Spare Bulbs").Photos);

        // Nothing that was on an item before is unreferenced after — the guarantee
        // the whole migration exists to keep.
        var referenced = await _context.ItemPhotos.Select(p => p.FileName).ToListAsync();
        Assert.Equal(["drill.jpg", "saw.jpg"], referenced.Order());
    }

    [Fact]
    public void AddItemPhotosDropsThePhotoPathColumn()
    {
        _context.Database.Migrate();

        Assert.DoesNotContain("PhotoPath", GetColumns("Items"));
    }

    [Fact]
    public void AddItemPhotosDownRestoresThePrimaryPhoto()
    {
        var migrator = _context.GetService<IMigrator>();
        migrator.Migrate();

        // Inserted with the primary second, so an ordering bug can't pass by luck.
        _context.Database.ExecuteSqlRaw(
            """
            INSERT INTO Items (Name, Quantity, CreatedAt) VALUES ('Drill', 1, datetime('now'));
            INSERT INTO ItemPhotos (ItemId, FileName, SortOrder)
            SELECT Id, 'second.jpg', 1 FROM Items WHERE Name = 'Drill'
            UNION ALL
            SELECT Id, 'primary.jpg', 0 FROM Items WHERE Name = 'Drill';
            """);

        migrator.Migrate(BeforePhotos);

        Assert.Contains("PhotoPath", GetColumns("Items"));

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT PhotoPath FROM Items WHERE Name = 'Drill'";

        // One column holds one photo, so the primary is the one that survives.
        Assert.Equal("primary.jpg", (string?)command.ExecuteScalar());
    }

    private List<string> GetColumns(string table)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";

        using var reader = command.ExecuteReader();

        var columns = new List<string>();
        while (reader.Read())
            columns.Add(reader.GetString(0));

        return columns;
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
