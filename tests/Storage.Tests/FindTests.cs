using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.Tests;

// The Find page's queries: a place filter that reaches everything inside it, tag
// filters that all have to hold, and text, together.
public class FindTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly ItemRepository _items;
    private readonly LocationRepository _locations;
    private readonly TagRepository _tags;

    public FindTests()
    {
        _items = new ItemRepository(_db);
        _locations = new LocationRepository(_db);
        _tags = new TagRepository(_db);
    }

    [Fact]
    public async Task APlaceFilterReachesEverythingInsideIt()
    {
        var garage = await _locations.AddAsync(new Location { Name = "Garage" });
        var shelf = await _locations.AddAsync(new Location { Name = "Shelf", ParentId = garage.Id });
        var bin = await _locations.AddAsync(new Location { Name = "Bin", ParentId = shelf.Id });
        var attic = await _locations.AddAsync(new Location { Name = "Attic" });

        await _items.AddAsync(new Item { Name = "Tyre gauge", LocationId = garage.Id });
        await _items.AddAsync(new Item { Name = "Winter tyres", LocationId = shelf.Id });
        await _items.AddAsync(new Item { Name = "Tyre valve caps", LocationId = bin.Id });
        await _items.AddAsync(new Item { Name = "Old tyre", LocationId = attic.Id });
        await _items.AddAsync(new Item { Name = "Loose tyre" });

        var inGarage = await _items.FindAsync(new ItemQuery("tyre", garage.Id, []));
        var inShelf = await _items.FindAsync(new ItemQuery("tyre", shelf.Id, []));
        var everywhere = await _items.CountMatchesAsync(new ItemQuery("tyre", null, []));

        Assert.Equal(["Tyre valve caps", "Winter tyres", "Tyre gauge"], inGarage.Select(i => i.Name));
        Assert.Equal(["Tyre valve caps", "Winter tyres"], inShelf.Select(i => i.Name));
        Assert.Equal(5, everywhere);
        Assert.Equal(3, await _items.CountMatchesAsync(new ItemQuery("tyre", garage.Id, [])));
    }

    [Fact]
    public async Task EveryTagFilterHasToHold()
    {
        var car = await _tags.AddAsync("Car", "#0277BD");
        var winter = await _tags.AddAsync("Winter", "#4E342E");

        var tyres = await _items.AddAsync(new Item { Name = "Winter tyres" });
        var mats = await _items.AddAsync(new Item { Name = "Car mats" });
        await _items.AddAsync(new Item { Name = "Scarf" });
        await _tags.SetItemTagsAsync(tyres.Id, ["Car", "Winter"]);
        await _tags.SetItemTagsAsync(mats.Id, ["Car"]);

        Assert.Equal(["Car mats", "Winter tyres"],
            (await _items.FindAsync(new ItemQuery(null, null, [car.Id]))).Select(i => i.Name).Order());
        Assert.Equal(["Winter tyres"],
            (await _items.FindAsync(new ItemQuery(null, null, [car.Id, winter.Id]))).Select(i => i.Name));
    }

    [Fact]
    public async Task NoFilterAtAllIsTheWholeInventoryNewestFirst()
    {
        await _items.AddAsync(new Item { Name = "First" });
        await _items.AddAsync(new Item { Name = "Second" });

        var all = await _items.FindAsync(new ItemQuery("  ", null, []));

        Assert.Equal(["Second", "First"], all.Select(i => i.Name));
    }

    [Fact]
    public async Task PlacesAreFoundUnderAPlaceButNotThePlaceItself()
    {
        var garage = await _locations.AddAsync(new Location { Name = "Garage shelf area" });
        var shelf = await _locations.AddAsync(new Location { Name = "Shelf 2", ParentId = garage.Id });
        await _locations.AddAsync(new Location { Name = "shelf box", ParentId = shelf.Id });
        await _locations.AddAsync(new Location { Name = "Attic shelf" });

        var under = await _locations.FindAsync("shelf", garage.Id);
        var everywhere = await _locations.FindAsync("shelf", null);

        Assert.Equal(["Shelf 2", "shelf box"], under.Select(l => l.Name));
        Assert.Equal(["Attic shelf", "Garage shelf area", "Shelf 2", "shelf box"], everywhere.Select(l => l.Name));
    }

    public void Dispose() => _db.Dispose();
}
