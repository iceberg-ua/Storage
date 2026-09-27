using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;
using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.Tests;

// Exercises the repositories against a real SQLite in-memory database so the
// actual provider, schema (via EnsureCreated), and FK relationships are tested.
public class RepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly StorageDbContext _context;

    public RepositoryTests()
    {
        // A ":memory:" SQLite DB lives only as long as the connection is open.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<StorageDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new StorageDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task CanInsertAndQueryLocation()
    {
        var locationRepo = new LocationRepository(_context);

        var saved = await locationRepo.AddAsync(new Location { Name = "Basement" });

        var fetched = await locationRepo.GetByIdAsync(saved.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Basement", fetched!.Name);
    }

    [Fact]
    public async Task CanInsertItemReferencingLocation()
    {
        var locationRepo = new LocationRepository(_context);
        var itemRepo = new ItemRepository(_context);

        var location = await locationRepo.AddAsync(new Location { Name = "Garage Shelf 2" });

        var item = await itemRepo.AddAsync(new Item
        {
            Name = "Cordless Drill",
            Description = "Bosch 18V",
            LocationId = location.Id
        });

        await itemRepo.SetItemPhotosAsync(item.Id, ["drill.jpg"]);

        var fetched = await itemRepo.GetByIdAsync(item.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Cordless Drill", fetched!.Name);
        Assert.Equal("drill.jpg", fetched.PrimaryPhotoFileName);
        Assert.NotEqual(default, fetched.CreatedAt);

        // Navigation property resolves the referenced location
        Assert.NotNull(fetched.Location);
        Assert.Equal("Garage Shelf 2", fetched.Location!.Name);
    }

    [Fact]
    public async Task GetByLocationIdReturnsOnlyMatchingItems()
    {
        var locationRepo = new LocationRepository(_context);
        var itemRepo = new ItemRepository(_context);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var attic = await locationRepo.AddAsync(new Location { Name = "Attic" });

        await itemRepo.AddAsync(new Item { Name = "Hammer", LocationId = garage.Id });
        await itemRepo.AddAsync(new Item { Name = "Ladder", LocationId = garage.Id });
        await itemRepo.AddAsync(new Item { Name = "Ornaments", LocationId = attic.Id });

        var garageItems = await itemRepo.GetByLocationIdAsync(garage.Id);

        Assert.Equal(2, garageItems.Count());
        Assert.All(garageItems, i => Assert.Equal(garage.Id, i.LocationId));
    }

    [Fact]
    public async Task ItemDefaultsToQuantityOne()
    {
        var itemRepo = new ItemRepository(_context);

        var item = await itemRepo.AddAsync(new Item { Name = "Screwdriver" });

        _context.ChangeTracker.Clear();
        var fetched = await itemRepo.GetByIdAsync(item.Id);

        Assert.NotNull(fetched);
        Assert.Equal(1, fetched!.Quantity);
    }

    [Fact]
    public async Task UpdateItemPersistsChanges()
    {
        var locationRepo = new LocationRepository(_context);
        var itemRepo = new ItemRepository(_context);

        var shed = await locationRepo.AddAsync(new Location { Name = "Shed" });
        var loft = await locationRepo.AddAsync(new Location { Name = "Loft" });

        var item = await itemRepo.AddAsync(new Item
        {
            Name = "Paint Tin",
            Description = "White emulsion",
            Quantity = 2,
            LocationId = shed.Id
        });

        item.Name = "Paint Tins";
        item.Description = "Magnolia emulsion";
        item.Quantity = 5;
        item.LocationId = loft.Id;
        await itemRepo.UpdateAsync(item);

        // Drop the tracked instances so the assertions read from the database.
        _context.ChangeTracker.Clear();
        var fetched = await itemRepo.GetByIdAsync(item.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Paint Tins", fetched!.Name);
        Assert.Equal("Magnolia emulsion", fetched.Description);
        Assert.Equal(5, fetched.Quantity);
        Assert.Equal(loft.Id, fetched.LocationId);
    }

    [Fact]
    public async Task UpdateLocationPersistsChanges()
    {
        var locationRepo = new LocationRepository(_context);

        var location = await locationRepo.AddAsync(new Location { Name = "Cellar" });

        location.Name = "Wine Cellar";
        await locationRepo.UpdateAsync(location);

        _context.ChangeTracker.Clear();
        var fetched = await locationRepo.GetByIdAsync(location.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Wine Cellar", fetched!.Name);
    }

    [Fact]
    public async Task GetPathReturnsTheChainRootFirst()
    {
        var locationRepo = new LocationRepository(_context);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf A", ParentId = garage.Id });
        var box = await locationRepo.AddAsync(new Location { Name = "Box 1", ParentId = shelf.Id });

        _context.ChangeTracker.Clear();
        var path = await locationRepo.GetPathAsync(box.Id);

        Assert.Equal(["Garage", "Shelf A", "Box 1"], path.Select(l => l.Name));
    }

    [Fact]
    public async Task GetPathSeesARenameSavedThroughAnotherContext()
    {
        // The app's LocationContext holds its repository for good, while the edit
        // screen saves through a context of its own — the header must still update.
        var longLived = new LocationRepository(_context);
        var shelf = await longLived.AddAsync(new Location { Name = "Shelf A" });
        await longLived.GetPathAsync(shelf.Id);

        using (var editContext = new StorageDbContext(
            new DbContextOptionsBuilder<StorageDbContext>().UseSqlite(_connection).Options))
        {
            var editRepo = new LocationRepository(editContext);
            var edited = await editRepo.GetByIdAsync(shelf.Id);
            edited!.Name = "Shelf B";
            await editRepo.UpdateAsync(edited);
        }

        var path = await longLived.GetPathAsync(shelf.Id);

        Assert.Equal("Shelf B", Assert.Single(path).Name);
    }

    [Fact]
    public async Task GetPathOfAMissingLocationIsEmpty()
    {
        var locationRepo = new LocationRepository(_context);

        Assert.Empty(await locationRepo.GetPathAsync(999));
    }

    [Fact]
    public async Task GetPathStopsAtACycle()
    {
        var locationRepo = new LocationRepository(_context);

        var a = await locationRepo.AddAsync(new Location { Name = "A" });
        var b = await locationRepo.AddAsync(new Location { Name = "B", ParentId = a.Id });

        // A moved under its own child.
        await CreateCycleAsync(a, b);
        var path = await locationRepo.GetPathAsync(b.Id);

        Assert.Equal(["A", "B"], path.Select(l => l.Name));
    }

    [Fact]
    public async Task GetDescendantIdsReturnsEveryLevelButNotSiblingsOrItself()
    {
        var locationRepo = new LocationRepository(_context);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf A", ParentId = garage.Id });
        var box = await locationRepo.AddAsync(new Location { Name = "Box 1", ParentId = shelf.Id });
        var bin = await locationRepo.AddAsync(new Location { Name = "Bin", ParentId = garage.Id });
        await locationRepo.AddAsync(new Location { Name = "Attic" });

        var descendants = await locationRepo.GetDescendantIdsAsync(garage.Id);

        Assert.Equal([shelf.Id, box.Id, bin.Id], descendants.Order());
        Assert.Empty(await locationRepo.GetDescendantIdsAsync(box.Id));
    }

    [Fact]
    public async Task GetDescendantIdsStopsAtACycle()
    {
        var locationRepo = new LocationRepository(_context);

        var a = await locationRepo.AddAsync(new Location { Name = "A" });
        var b = await locationRepo.AddAsync(new Location { Name = "B", ParentId = a.Id });

        await CreateCycleAsync(a, b);

        Assert.Equal([b.Id], await locationRepo.GetDescendantIdsAsync(a.Id));
    }

    [Fact]
    public async Task UpdateRejectsMovingALocationUnderItsOwnDescendant()
    {
        var locationRepo = new LocationRepository(_context);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf A", ParentId = garage.Id });
        var box = await locationRepo.AddAsync(new Location { Name = "Box 1", ParentId = shelf.Id });

        garage.ParentId = box.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => locationRepo.UpdateAsync(garage));

        _context.ChangeTracker.Clear();
        var fetched = await locationRepo.GetByIdAsync(garage.Id);
        Assert.Null(fetched!.ParentId);
    }

    [Fact]
    public async Task UpdateRejectsMakingALocationItsOwnParent()
    {
        var locationRepo = new LocationRepository(_context);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });

        garage.ParentId = garage.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => locationRepo.UpdateAsync(garage));
    }

    [Fact]
    public async Task UpdateAllowsMovingALocationUnderASibling()
    {
        var locationRepo = new LocationRepository(_context);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf A", ParentId = garage.Id });
        var bin = await locationRepo.AddAsync(new Location { Name = "Bin", ParentId = garage.Id });

        bin.ParentId = shelf.Id;
        await locationRepo.UpdateAsync(bin);

        _context.ChangeTracker.Clear();
        var path = await locationRepo.GetPathAsync(bin.Id);
        Assert.Equal(["Garage", "Shelf A", "Bin"], path.Select(l => l.Name));
    }

    // Writes a loop straight to the table, bypassing UpdateAsync's check — the state a
    // database saved before that check existed can be in.
    private async Task CreateCycleAsync(Location child, Location parent)
    {
        await _context.Locations
            .Where(l => l.Id == child.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ParentId, parent.Id));

        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task SetItemPhotosStoresThemInTheGivenOrderWithTheFirstAsPrimary()
    {
        var itemRepo = new ItemRepository(_context);

        var item = await itemRepo.AddAsync(new Item { Name = "Cordless Drill" });

        await itemRepo.SetItemPhotosAsync(item.Id, ["front.jpg", "back.jpg", "serial.jpg"]);

        _context.ChangeTracker.Clear();
        var fetched = await itemRepo.GetByIdAsync(item.Id);

        Assert.NotNull(fetched);
        Assert.Equal(["front.jpg", "back.jpg", "serial.jpg"], fetched!.Photos.Select(p => p.FileName));
        Assert.Equal([0, 1, 2], fetched.Photos.Select(p => p.SortOrder));

        // The thumbnail the list shows is the first of the set, not whichever row
        // happens to come back first.
        Assert.Equal("front.jpg", fetched.PrimaryPhotoFileName);
    }

    [Fact]
    public async Task GetAllReturnsPhotosInSortOrder()
    {
        var itemRepo = new ItemRepository(_context);

        var item = await itemRepo.AddAsync(new Item { Name = "Cordless Drill" });
        await itemRepo.SetItemPhotosAsync(item.Id, ["front.jpg", "back.jpg"]);

        // Rewritten so the rows land in the table in the opposite order to the one
        // they should come back in — otherwise insertion order would pass for sorting.
        await itemRepo.SetItemPhotosAsync(item.Id, ["back.jpg", "front.jpg"]);

        _context.ChangeTracker.Clear();
        var fetched = (await itemRepo.GetAllAsync()).Single();

        Assert.Equal(["back.jpg", "front.jpg"], fetched.Photos.Select(p => p.FileName));
        Assert.Equal("back.jpg", fetched.PrimaryPhotoFileName);
    }

    [Fact]
    public async Task SetItemPhotosReplacesTheWholeSetAndRenumbersIt()
    {
        var itemRepo = new ItemRepository(_context);

        var item = await itemRepo.AddAsync(new Item { Name = "Cordless Drill" });
        await itemRepo.SetItemPhotosAsync(item.Id, ["front.jpg", "back.jpg", "serial.jpg"]);

        // The middle one removed and the survivors swapped.
        await itemRepo.SetItemPhotosAsync(item.Id, ["serial.jpg", "front.jpg"]);

        _context.ChangeTracker.Clear();
        var fetched = await itemRepo.GetByIdAsync(item.Id);

        Assert.NotNull(fetched);
        Assert.Equal(["serial.jpg", "front.jpg"], fetched!.Photos.Select(p => p.FileName));

        // Nothing left behind by the rows that went, and no gap in the order.
        Assert.Equal([0, 1], fetched.Photos.Select(p => p.SortOrder));
        Assert.Equal(2, await _context.ItemPhotos.CountAsync());
    }

    [Fact]
    public async Task SetItemPhotosDropsBlanksAndRepeats()
    {
        var itemRepo = new ItemRepository(_context);

        var item = await itemRepo.AddAsync(new Item { Name = "Cordless Drill" });

        await itemRepo.SetItemPhotosAsync(item.Id, ["front.jpg", "  ", "front.jpg", "back.jpg", ""]);

        _context.ChangeTracker.Clear();
        var fetched = await itemRepo.GetByIdAsync(item.Id);

        // A blank names no file, and the same file twice is one photo shown twice.
        Assert.NotNull(fetched);
        Assert.Equal(["front.jpg", "back.jpg"], fetched!.Photos.Select(p => p.FileName));
    }

    [Fact]
    public async Task DeletingAnItemDeletesItsPhotoRows()
    {
        var itemRepo = new ItemRepository(_context);

        var item = await itemRepo.AddAsync(new Item { Name = "Cordless Drill" });
        await itemRepo.SetItemPhotosAsync(item.Id, ["front.jpg", "back.jpg"]);

        await itemRepo.DeleteAsync(item.Id);

        // The files themselves are the caller's to remove; the rows go by cascade,
        // which is what keeps the orphan sweep's "referenced" set honest.
        Assert.Empty(await _context.ItemPhotos.ToListAsync());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
