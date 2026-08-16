using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;
using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.Tests;

// Same fixture shape as RepositoryTests: a real SQLite in-memory database, so the
// NOCASE collation and the unique index on Tag.Name are actually exercised.
public class TagRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly StorageDbContext _context;
    private readonly ItemRepository _itemRepo;
    private readonly TagRepository _tagRepo;

    public TagRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<StorageDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new StorageDbContext(options);
        _context.Database.EnsureCreated();

        _itemRepo = new ItemRepository(_context);
        _tagRepo = new TagRepository(_context);
    }

    [Fact]
    public async Task SetItemTagsCreatesAndAssignsTags()
    {
        var item = await _itemRepo.AddAsync(new Item { Name = "Cordless Drill" });

        await _tagRepo.SetItemTagsAsync(item.Id, ["tools", "power"]);

        _context.ChangeTracker.Clear();
        var fetched = await _itemRepo.GetByIdAsync(item.Id);

        Assert.NotNull(fetched);
        Assert.Equal(["power", "tools"], fetched!.Tags.Select(t => t.Name).Order());
        Assert.Equal(2, await _context.Tags.CountAsync());
    }

    [Fact]
    public async Task TagNamesAreDeDupedCaseInsensitively()
    {
        var drill = await _itemRepo.AddAsync(new Item { Name = "Drill" });
        var saw = await _itemRepo.AddAsync(new Item { Name = "Saw" });

        await _tagRepo.SetItemTagsAsync(drill.Id, ["Tools"]);
        await _tagRepo.SetItemTagsAsync(saw.Id, ["tools"]);

        _context.ChangeTracker.Clear();

        // One row, under the casing it was first stored with.
        var tags = await _context.Tags.ToListAsync();
        Assert.Single(tags);
        Assert.Equal("Tools", tags[0].Name);

        // Both items point at it.
        var fetchedSaw = await _itemRepo.GetByIdAsync(saw.Id);
        Assert.Equal(tags[0].Id, Assert.Single(fetchedSaw!.Tags).Id);
    }

    [Fact]
    public async Task SetItemTagsCollapsesCaseVariantsWithinOneCall()
    {
        var item = await _itemRepo.AddAsync(new Item { Name = "Drill" });

        await _tagRepo.SetItemTagsAsync(item.Id, ["Tools", "tools", "  TOOLS  "]);

        _context.ChangeTracker.Clear();
        var fetched = await _itemRepo.GetByIdAsync(item.Id);

        Assert.Equal("Tools", Assert.Single(fetched!.Tags).Name);
        Assert.Equal(1, await _context.Tags.CountAsync());
    }

    [Fact]
    public async Task SetItemTagsIgnoresBlankNames()
    {
        var item = await _itemRepo.AddAsync(new Item { Name = "Drill" });

        await _tagRepo.SetItemTagsAsync(item.Id, ["tools", "  ", ""]);

        _context.ChangeTracker.Clear();
        var fetched = await _itemRepo.GetByIdAsync(item.Id);

        Assert.Equal("tools", Assert.Single(fetched!.Tags).Name);
    }

    [Fact]
    public async Task SetItemTagsReplacesPreviousAssignment()
    {
        var item = await _itemRepo.AddAsync(new Item { Name = "Drill" });

        await _tagRepo.SetItemTagsAsync(item.Id, ["tools", "power"]);
        await _tagRepo.SetItemTagsAsync(item.Id, ["power", "bosch"]);

        _context.ChangeTracker.Clear();
        var fetched = await _itemRepo.GetByIdAsync(item.Id);

        Assert.Equal(["bosch", "power"], fetched!.Tags.Select(t => t.Name).Order());

        // "tools" is on no item now, and is deliberately kept: it stays available
        // for reuse and in autocomplete.
        Assert.Equal(3, await _context.Tags.CountAsync());
    }

    [Fact]
    public async Task DeletingAnItemLeavesTheTagsBehind()
    {
        var item = await _itemRepo.AddAsync(new Item { Name = "Drill" });
        await _tagRepo.SetItemTagsAsync(item.Id, ["tools"]);

        await _itemRepo.DeleteAsync(item.Id);

        _context.ChangeTracker.Clear();
        Assert.Equal(1, await _context.Tags.CountAsync());
        Assert.Null(await _itemRepo.GetByIdAsync(item.Id));
    }

    [Fact]
    public async Task GetAllTagsReturnsTagsOrderedByName()
    {
        var item = await _itemRepo.AddAsync(new Item { Name = "Drill" });
        await _tagRepo.SetItemTagsAsync(item.Id, ["tools", "bosch", "power"]);

        var tags = await _tagRepo.GetAllAsync();

        Assert.Equal(["bosch", "power", "tools"], tags.Select(t => t.Name));
    }

    [Fact]
    public async Task SetItemTagsOnMissingItemDoesNothing()
    {
        await _tagRepo.SetItemTagsAsync(999, ["tools"]);

        Assert.Equal(0, await _context.Tags.CountAsync());
    }

    [Fact]
    public async Task NewTagsGetAPaletteColour()
    {
        var item = await _itemRepo.AddAsync(new Item { Name = "Drill" });

        await _tagRepo.SetItemTagsAsync(item.Id, ["tools"]);

        _context.ChangeTracker.Clear();
        var tag = await _context.Tags.SingleAsync();

        Assert.Contains(tag.Color, TagPalette.Colors);
    }

    [Fact]
    public async Task TwoNewTagsInOneSaveGetDifferentColours()
    {
        var item = await _itemRepo.AddAsync(new Item { Name = "Drill" });

        await _tagRepo.SetItemTagsAsync(item.Id, ["tools", "power"]);

        _context.ChangeTracker.Clear();
        var colors = await _context.Tags.Select(t => t.Color).ToListAsync();

        Assert.Equal(2, colors.Distinct().Count());
    }

    [Fact]
    public async Task ExistingTagKeepsItsColourWhenReused()
    {
        var drill = await _itemRepo.AddAsync(new Item { Name = "Drill" });
        var saw = await _itemRepo.AddAsync(new Item { Name = "Saw" });

        await _tagRepo.SetItemTagsAsync(drill.Id, ["tools"]);

        _context.ChangeTracker.Clear();
        var original = (await _context.Tags.SingleAsync()).Color;

        await _tagRepo.SetItemTagsAsync(saw.Id, ["TOOLS"]);

        _context.ChangeTracker.Clear();
        var tag = await _context.Tags.SingleAsync();

        Assert.Equal(original, tag.Color);
    }

    [Fact]
    public async Task AddAndRenamePersistNameAndColour()
    {
        var tag = await _tagRepo.AddAsync("  tools  ", TagPalette.Colors[2]);

        Assert.Equal("tools", tag.Name);

        tag.Name = "Power Tools";
        tag.Color = TagPalette.Colors[5];
        await _tagRepo.UpdateAsync(tag);

        _context.ChangeTracker.Clear();
        var fetched = await _tagRepo.GetByIdAsync(tag.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Power Tools", fetched!.Name);
        Assert.Equal(TagPalette.Colors[5], fetched.Color);
    }

    [Fact]
    public async Task NameExistsIsCaseInsensitiveAndCanExcludeATag()
    {
        var tag = await _tagRepo.AddAsync("books", TagPalette.Default);

        Assert.True(await _tagRepo.NameExistsAsync("BOOKS"));
        Assert.True(await _tagRepo.NameExistsAsync("  Books  "));
        Assert.False(await _tagRepo.NameExistsAsync("magazines"));

        // A tag is allowed to keep its own name — recolouring must not read as a clash.
        Assert.False(await _tagRepo.NameExistsAsync("Books", excludingId: tag.Id));
    }

    [Fact]
    public async Task DeleteTagRemovesItFromItsItemsButKeepsThem()
    {
        var drill = await _itemRepo.AddAsync(new Item { Name = "Drill" });
        var saw = await _itemRepo.AddAsync(new Item { Name = "Saw" });

        await _tagRepo.SetItemTagsAsync(drill.Id, ["tools", "power"]);
        await _tagRepo.SetItemTagsAsync(saw.Id, ["tools"]);

        var tools = await _context.Tags.SingleAsync(t => t.Name == "tools");
        await _tagRepo.DeleteAsync(tools.Id);

        _context.ChangeTracker.Clear();

        // The tag and its joins are gone; "power" and both items are not.
        Assert.Equal("power", (await _context.Tags.SingleAsync()).Name);
        Assert.Equal("power", Assert.Single((await _itemRepo.GetByIdAsync(drill.Id))!.Tags).Name);
        Assert.Empty((await _itemRepo.GetByIdAsync(saw.Id))!.Tags);
    }

    [Fact]
    public async Task GetAllWithItemCountsLoadsTheItems()
    {
        var drill = await _itemRepo.AddAsync(new Item { Name = "Drill" });
        var saw = await _itemRepo.AddAsync(new Item { Name = "Saw" });

        await _tagRepo.SetItemTagsAsync(drill.Id, ["tools"]);
        await _tagRepo.SetItemTagsAsync(saw.Id, ["tools"]);

        _context.ChangeTracker.Clear();
        var tags = await _tagRepo.GetAllWithItemCountsAsync();

        Assert.Equal(2, Assert.Single(tags).Items.Count);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
