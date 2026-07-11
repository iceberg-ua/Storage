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
            Tags = "tools, power, bosch",
            PhotoPath = "/photos/drill.jpg",
            LocationId = location.Id
        });

        var fetched = await itemRepo.GetByIdAsync(item.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Cordless Drill", fetched!.Name);
        Assert.Equal("tools, power, bosch", fetched.Tags);
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

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
