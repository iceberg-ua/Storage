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

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
