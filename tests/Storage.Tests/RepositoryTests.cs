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
            PhotoPath = "/photos/drill.jpg",
            LocationId = location.Id
        });

        var fetched = await itemRepo.GetByIdAsync(item.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Cordless Drill", fetched!.Name);
        Assert.Equal("/photos/drill.jpg", fetched.PhotoPath);
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

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
