using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;
using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.Tests;

// Search runs against a real SQLite database, because the thing under test is mostly
// SQL translation: the LIKE pattern, the escape character, and the unicode_lower
// function the interceptor registers on the connection.
public class ItemSearchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly StorageDbContext _context;
    private readonly ItemRepository _items;

    public ItemSearchTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // The interceptor only fires for connections EF opens; this one was opened
        // here, so the function has to be registered on it directly.
        UnicodeLowerInterceptor.Register(_connection);

        var options = new DbContextOptionsBuilder<StorageDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new StorageDbContext(options);
        _context.Database.EnsureCreated();
        _items = new ItemRepository(_context);
    }

    private async Task<Item> SeedAsync(
        string name,
        string? description = null,
        int? locationId = null,
        params string[] tags)
    {
        var item = new Item
        {
            Name = name,
            Description = description,
            LocationId = locationId,
            Tags = tags.Select(t => new Tag { Name = t }).ToList()
        };

        _context.Items.Add(item);
        await _context.SaveChangesAsync();
        return item;
    }

    [Fact]
    public async Task NameScopeMatchesOnlyTheName()
    {
        await SeedAsync("Hammer");
        await SeedAsync("Screwdriver", description: "next to the hammer");

        var results = await _items.SearchAsync("hammer", SearchScope.Name);

        Assert.Equal(["Hammer"], results.Select(i => i.Name));
    }

    [Fact]
    public async Task DescriptionScopeMatchesOnlyTheDescription()
    {
        await SeedAsync("Hammer");
        await SeedAsync("Screwdriver", description: "next to the hammer");

        var results = await _items.SearchAsync("hammer", SearchScope.Description);

        Assert.Equal(["Screwdriver"], results.Select(i => i.Name));
    }

    [Fact]
    public async Task TagScopeMatchesOnlyTags()
    {
        await SeedAsync("Hammer", tags: "tools");
        await SeedAsync("Toolbox", description: "holds the tools");

        var results = await _items.SearchAsync("tools", SearchScope.Tags);

        Assert.Equal(["Hammer"], results.Select(i => i.Name));
    }

    [Fact]
    public async Task EverythingSpansAllThreeFields()
    {
        await SeedAsync("Hammer");
        await SeedAsync("Screwdriver", description: "next to the hammer");
        await SeedAsync("Toolbox", tags: "hammer");
        await SeedAsync("Kettle");

        var results = await _items.SearchAsync("hammer", SearchScope.Everything);

        Assert.Equal(
            ["Hammer", "Screwdriver", "Toolbox"],
            results.Select(i => i.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task ItemMatchingSeveralFieldsIsReturnedOnce()
    {
        await SeedAsync("Hammer", description: "a hammer", tags: "hammer");

        var results = await _items.SearchAsync("hammer", SearchScope.Everything);

        Assert.Single(results);
    }

    [Fact]
    public async Task MatchesArePartial()
    {
        await SeedAsync("Sledgehammer");

        var results = await _items.SearchAsync("ledgeham", SearchScope.Name);

        Assert.Single(results);
    }

    [Theory]
    // Latin, to prove the ASCII path still works.
    [InlineData("Hammer", "hammer")]
    [InlineData("hammer", "HAMMER")]
    // Cyrillic: what SQLite's own lower() and NOCASE collation cannot do.
    [InlineData("Молоток", "молоток")]
    [InlineData("молоток", "МОЛОТОК")]
    [InlineData("Викрутка", "ВИКРУТКА")]
    // German, where the folded form differs in length from the original.
    [InlineData("STRASSE", "strasse")]
    // Greek.
    [InlineData("Σφυρί", "ΣΦΥΡΊ")]
    public async Task CaseIsIgnoredBeyondAscii(string stored, string query)
    {
        await SeedAsync(stored);

        var results = await _items.SearchAsync(query, SearchScope.Name);

        Assert.Single(results);
    }

    [Fact]
    public async Task CaseIsIgnoredForTagsToo()
    {
        await SeedAsync("Молоток", tags: "Інструменти");

        var results = await _items.SearchAsync("інструменти", SearchScope.Tags);

        Assert.Single(results);
    }

    [Fact]
    public async Task BlankQueryReturnsEverything()
    {
        await SeedAsync("Hammer");
        await SeedAsync("Kettle");

        Assert.Equal(2, (await _items.SearchAsync("", SearchScope.Name)).Count());
        Assert.Equal(2, (await _items.SearchAsync("   ", SearchScope.Name)).Count());
        Assert.Equal(2, (await _items.SearchAsync(null, SearchScope.Name)).Count());
    }

    [Fact]
    public async Task WildcardCharactersAreMatchedLiterally()
    {
        await SeedAsync("50% cotton");
        await SeedAsync("50 pence");

        var percent = await _items.SearchAsync("50%", SearchScope.Name);
        Assert.Equal(["50% cotton"], percent.Select(i => i.Name));

        await SeedAsync("a_b");
        await SeedAsync("axb");

        var underscore = await _items.SearchAsync("a_b", SearchScope.Name);
        Assert.Equal(["a_b"], underscore.Select(i => i.Name));
    }

    [Fact]
    public async Task SearchCanBeScopedToOneLocation()
    {
        var garage = new Location { Name = "Garage" };
        var attic = new Location { Name = "Attic" };
        _context.Locations.AddRange(garage, attic);
        await _context.SaveChangesAsync();

        await SeedAsync("Hammer", locationId: garage.Id);
        await SeedAsync("Hammer drill", locationId: attic.Id);

        var scoped = await _items.SearchAsync("hammer", SearchScope.Everything, garage.Id);
        Assert.Equal(["Hammer"], scoped.Select(i => i.Name));

        var global = await _items.SearchAsync("hammer", SearchScope.Everything);
        Assert.Equal(2, global.Count());
    }

    [Fact]
    public async Task BlankQueryInALocationStillNarrowsToThatLocation()
    {
        var garage = new Location { Name = "Garage" };
        _context.Locations.Add(garage);
        await _context.SaveChangesAsync();

        await SeedAsync("Hammer", locationId: garage.Id);
        await SeedAsync("Kettle");

        var results = await _items.SearchAsync("", SearchScope.Everything, garage.Id);

        Assert.Equal(["Hammer"], results.Select(i => i.Name));
    }

    [Fact]
    public async Task ResultsCarryLocationTagsAndPhotos()
    {
        var garage = new Location { Name = "Garage" };
        _context.Locations.Add(garage);
        await _context.SaveChangesAsync();

        var item = await SeedAsync("Hammer", locationId: garage.Id, tags: "tools");
        await _items.SetItemPhotosAsync(item.Id, ["b.jpg", "a.jpg"]);

        var found = Assert.Single(await _items.SearchAsync("hammer", SearchScope.Name));

        Assert.Equal("Garage", found.Location?.Name);
        Assert.Equal("tools", Assert.Single(found.Tags).Name);
        Assert.Equal("b.jpg", found.PrimaryPhotoFileName);
    }

    [Fact]
    public async Task NoMatchReturnsEmptyRatherThanEverything()
    {
        await SeedAsync("Hammer");

        Assert.Empty(await _items.SearchAsync("kettle", SearchScope.Everything));
    }

    // --- Location search ----------------------------------------------------

    private async Task<Location> SeedLocationAsync(string name, int? parentId = null)
    {
        var location = new Location { Name = name, ParentId = parentId };
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();
        return location;
    }

    [Fact]
    public async Task LocationSearchMatchesNamePartially()
    {
        var locations = new LocationRepository(_context);
        await SeedLocationAsync("Garage");
        await SeedLocationAsync("Attic");

        var results = await locations.SearchAsync("gara");

        Assert.Equal(["Garage"], results.Select(l => l.Name));
    }

    [Theory]
    [InlineData("Garage", "GARAGE")]
    [InlineData("Підвал", "ПІДВАЛ")]
    [InlineData("ГОРИЩЕ", "горище")]
    public async Task LocationSearchIgnoresCaseBeyondAscii(string stored, string query)
    {
        var locations = new LocationRepository(_context);
        await SeedLocationAsync(stored);

        Assert.Single(await locations.SearchAsync(query));
    }

    [Fact]
    public async Task LocationSearchCanBeScopedToChildren()
    {
        var locations = new LocationRepository(_context);
        var cellar = await SeedLocationAsync("Cellar");
        await SeedLocationAsync("Shelf A", cellar.Id);
        await SeedLocationAsync("Shelf B");

        var scoped = await locations.SearchAsync("shelf", cellar.Id);
        Assert.Equal(["Shelf A"], scoped.Select(l => l.Name));

        var everywhere = await locations.SearchAsync("shelf");
        Assert.Equal(2, everywhere.Count());
    }

    [Fact]
    public async Task LocationSearchBlankQueryReturnsAll()
    {
        var locations = new LocationRepository(_context);
        await SeedLocationAsync("Garage");
        await SeedLocationAsync("Attic");

        Assert.Equal(2, (await locations.SearchAsync("")).Count());
        Assert.Equal(2, (await locations.SearchAsync(null)).Count());
    }

    [Fact]
    public async Task LocationSearchResultsCarryParent()
    {
        var locations = new LocationRepository(_context);
        var cellar = await SeedLocationAsync("Cellar");
        await SeedLocationAsync("Shelf", cellar.Id);

        var found = Assert.Single(await locations.SearchAsync("shelf"));

        Assert.Equal("Cellar", found.Parent?.Name);
    }

    [Fact]
    public async Task LocationSearchTreatsWildcardsLiterally()
    {
        var locations = new LocationRepository(_context);
        await SeedLocationAsync("Box_1");
        await SeedLocationAsync("Box21");

        var results = await locations.SearchAsync("box_");

        Assert.Equal(["Box_1"], results.Select(l => l.Name));
    }

    // The fixture above registers unicode_lower by hand, which is not how the app gets
    // it — there, EF opens the connection and the interceptor does it. That path has
    // its own failure mode ("no such function"), so it is worth its own test against a
    // real file-backed database opened by EF itself.
    [Fact]
    public async Task InterceptorRegistersTheFunctionOnConnectionsEfOpens()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"storage-search-{Guid.NewGuid():N}.db");

        var options = new DbContextOptionsBuilder<StorageDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .AddInterceptors(new UnicodeLowerInterceptor())
            .Options;

        try
        {
            using var context = new StorageDbContext(options);
            await context.Database.EnsureCreatedAsync();

            context.Items.Add(new Item { Name = "Молоток" });
            await context.SaveChangesAsync();

            var repository = new ItemRepository(context);
            var results = await repository.SearchAsync("МОЛОТОК", SearchScope.Name);

            Assert.Single(results);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
