using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;
using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.Tests;

// Exercises the repositories against a real SQLite database (see TestDatabase). The
// repositories get the factory, as in the app; _context is a separate one the tests
// use to seed and inspect rows behind the repositories' backs.
public class RepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly StorageDbContext _context;

    public RepositoryTests()
    {
        _context = _db.CreateDbContext();
    }

    [Fact]
    public async Task CanInsertAndQueryLocation()
    {
        var locationRepo = new LocationRepository(_db);

        var saved = await locationRepo.AddAsync(new Location { Name = "Basement" });

        var fetched = await locationRepo.GetByIdAsync(saved.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Basement", fetched!.Name);
    }

    [Fact]
    public async Task CanInsertItemReferencingLocation()
    {
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);

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
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);

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
        var itemRepo = new ItemRepository(_db);

        var item = await itemRepo.AddAsync(new Item { Name = "Screwdriver" });

        var fetched = await itemRepo.GetByIdAsync(item.Id);

        Assert.NotNull(fetched);
        Assert.Equal(1, fetched!.Quantity);
    }

    [Fact]
    public async Task UpdateItemPersistsChanges()
    {
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);

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
        var locationRepo = new LocationRepository(_db);

        var location = await locationRepo.AddAsync(new Location { Name = "Cellar" });

        location.Name = "Wine Cellar";
        await locationRepo.UpdateAsync(location);

        var fetched = await locationRepo.GetByIdAsync(location.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Wine Cellar", fetched!.Name);
    }

    [Fact]
    public async Task UpdateOfAMissingLocationThrows()
    {
        var locationRepo = new LocationRepository(_db);

        var cellar = await locationRepo.AddAsync(new Location { Name = "Cellar" });

        // Deleted elsewhere while the edit screen was open.
        await _context.Locations.Where(l => l.Id == cellar.Id).ExecuteDeleteAsync();

        cellar.Name = "Wine Cellar";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => locationRepo.UpdateAsync(cellar));

        // Not quietly re-created by the update.
        Assert.Empty(_context.Locations.AsNoTracking());
    }

    [Fact]
    public async Task AddingAnItemWithLoadedRelationsDoesNotInsertThemAgain()
    {
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);
        var tagRepo = new TagRepository(_db);

        await locationRepo.AddAsync(new Location { Name = "Garage" });
        await tagRepo.AddAsync("tools", TagPalette.Default);

        // Both loaded by earlier calls, so their contexts are long gone.
        var garage = (await locationRepo.GetAllAsync()).Single();
        var tools = (await tagRepo.GetAllAsync()).Single();

        var drill = await itemRepo.AddAsync(new Item
        {
            Name = "Drill",
            Location = garage,
            Tags = [tools, new Tag { Name = "power", Color = TagPalette.Default }],
            Photos = [new ItemPhoto { FileName = "drill.jpg" }]
        });

        Assert.Equal(1, await _context.Locations.CountAsync());
        Assert.Equal(["power", "tools"], _context.Tags.AsNoTracking().Select(t => t.Name).Order());

        var fetched = await itemRepo.GetByIdAsync(drill.Id);
        Assert.Equal(garage.Id, fetched!.LocationId);
        Assert.Equal(2, fetched.Tags.Count);
        Assert.Equal("drill.jpg", fetched.PrimaryPhotoFileName);
    }

    [Fact]
    public async Task LocationDescriptionIsSavedChangedAndCleared()
    {
        var locationRepo = new LocationRepository(_db);

        var location = await locationRepo.AddAsync(
            new Location { Name = "Cellar", Description = "Behind the stairs" });

        var fetched = await locationRepo.GetByIdAsync(location.Id);
        Assert.Equal("Behind the stairs", fetched!.Description);

        fetched.Description = "Left of the boiler";
        await locationRepo.UpdateAsync(fetched);
        Assert.Equal("Left of the boiler", (await locationRepo.GetByIdAsync(location.Id))!.Description);

        fetched = await locationRepo.GetByIdAsync(location.Id);
        fetched!.Description = null;
        await locationRepo.UpdateAsync(fetched);
        Assert.Null((await locationRepo.GetByIdAsync(location.Id))!.Description);
    }

    [Fact]
    public async Task LocationPhotoIsSavedReplacedAndCleared()
    {
        var locationRepo = new LocationRepository(_db);

        var location = await locationRepo.AddAsync(
            new Location { Name = "Garage Shelf", PhotoFileName = "shelf.jpg" });

        var fetched = await locationRepo.GetByIdAsync(location.Id);
        Assert.Equal("shelf.jpg", fetched!.PhotoFileName);

        fetched.PhotoFileName = "shelf-tidied.jpg";
        await locationRepo.UpdateAsync(fetched);
        Assert.Equal("shelf-tidied.jpg", (await locationRepo.GetByIdAsync(location.Id))!.PhotoFileName);

        fetched = await locationRepo.GetByIdAsync(location.Id);
        fetched!.PhotoFileName = null;
        await locationRepo.UpdateAsync(fetched);
        Assert.Null((await locationRepo.GetByIdAsync(location.Id))!.PhotoFileName);
    }

    [Fact]
    public async Task DeleteLocationHandsBackItsPhotoForTheCallerToRemove()
    {
        var locationRepo = new LocationRepository(_db);

        var location = await locationRepo.AddAsync(
            new Location { Name = "Attic", PhotoFileName = "attic.jpg" });

        var deleted = await locationRepo.DeleteAsync(location.Id);

        Assert.Equal("attic.jpg", deleted?.PhotoFileName);
        Assert.Empty(_context.Locations.AsNoTracking());
        Assert.Null(await locationRepo.DeleteAsync(location.Id));
    }

    [Fact]
    public async Task RefusedLocationDeleteDoesNotBlockLaterSaves()
    {
        var locationRepo = new LocationRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        await locationRepo.AddAsync(new Location { Name = "Shelf", ParentId = garage.Id });

        // The delete's own context has never loaded the child, so EF can't null its
        // ParentId and SQLite's foreign key refuses the delete.
        await Assert.ThrowsAsync<DbUpdateException>(() => locationRepo.DeleteAsync(garage.Id));

        // A later save still works, and the refused delete took nothing with it.
        await locationRepo.AddAsync(new Location { Name = "Attic" });

        Assert.Equal(["Attic", "Garage", "Shelf"], _context.Locations.AsNoTracking().Select(l => l.Name).Order());
    }

    [Fact]
    public async Task ReferencedPhotosIncludeLocationPhotos()
    {
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);

        // The orphan sweep keeps only what this returns, so a location photo missing
        // from it would be deleted on the next launch.
        var garage = await locationRepo.AddAsync(new Location { Name = "Garage", PhotoFileName = "garage.jpg" });
        await locationRepo.AddAsync(new Location { Name = "Attic" });

        var drill = await itemRepo.AddAsync(new Item { Name = "Drill", LocationId = garage.Id });
        await itemRepo.SetItemPhotosAsync(drill.Id, ["drill.jpg", "drill-label.jpg"]);

        var referenced = await _context.GetReferencedPhotoFileNamesAsync();

        Assert.Equal(["drill-label.jpg", "drill.jpg", "garage.jpg"], referenced.Order());
    }

    [Fact]
    public async Task GetPathReturnsTheChainRootFirst()
    {
        var locationRepo = new LocationRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf A", ParentId = garage.Id });
        var box = await locationRepo.AddAsync(new Location { Name = "Box 1", ParentId = shelf.Id });

        var path = await locationRepo.GetPathAsync(box.Id);

        Assert.Equal(["Garage", "Shelf A", "Box 1"], path.Select(l => l.Name));
    }

    [Fact]
    public async Task GetPathSeesARenameSavedThroughAnotherRepository()
    {
        // A location's page holds its repository for good, while the edit screen saves
        // through one of its own — the header must still update.
        var longLived = new LocationRepository(_db);
        var shelf = await longLived.AddAsync(new Location { Name = "Shelf A" });
        await longLived.GetPathAsync(shelf.Id);

        var editRepo = new LocationRepository(_db);
        var edited = await editRepo.GetByIdAsync(shelf.Id);
        edited!.Name = "Shelf B";
        await editRepo.UpdateAsync(edited);

        var path = await longLived.GetPathAsync(shelf.Id);

        Assert.Equal("Shelf B", Assert.Single(path).Name);
    }

    [Fact]
    public async Task ReadsSeeRowsChangedSinceTheLastRead()
    {
        var itemRepo = new ItemRepository(_db);
        var item = await itemRepo.AddAsync(new Item { Name = "Drill" });
        await itemRepo.GetByIdAsync(item.Id);

        // A long-lived context would hand back the instance it cached on the read above.
        await _context.Items
            .Where(i => i.Id == item.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Name, "Hammer Drill"));

        Assert.Equal("Hammer Drill", (await itemRepo.GetByIdAsync(item.Id))!.Name);
    }

    [Fact]
    public async Task PagesCanLoadAtTheSameTime()
    {
        // What Home and the Items tab do when one is opened while the other still
        // loads: one repository instance each, several queries in flight at once.
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        for (var i = 0; i < 20; i++)
            await itemRepo.AddAsync(new Item { Name = $"Thing {i}", LocationId = garage.Id });

        var loads = Enumerable.Range(0, 10).SelectMany(_ => new Task[]
        {
            itemRepo.GetAllAsync(),
            itemRepo.CountAsync(),
            itemRepo.GetRecentAsync(5),
            locationRepo.GetTopLevelCardsAsync(),
            locationRepo.CountAsync()
        });

        await Task.WhenAll(loads);
    }

    [Fact]
    public async Task UpdateWritesOnlyTheLocationNotWhatItWasLoadedWith()
    {
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf", ParentId = garage.Id });
        await itemRepo.AddAsync(new Item { Name = "Drill", LocationId = shelf.Id });

        // Loaded with parent, children and items, as the edit screen does. Edits to
        // that graph are not this save's to write.
        var loaded = await locationRepo.GetByIdAsync(shelf.Id);
        loaded!.Name = "Top Shelf";
        loaded.Parent!.Name = "Not saved";
        loaded.Items.Single().Name = "Not saved either";
        await locationRepo.UpdateAsync(loaded);

        Assert.Equal(
            ["Garage", "Top Shelf"],
            _context.Locations.AsNoTracking().Select(l => l.Name).Order());
        Assert.Equal("Drill", (await _context.Items.AsNoTracking().SingleAsync()).Name);
    }

    [Fact]
    public async Task GetPathOfAMissingLocationIsEmpty()
    {
        var locationRepo = new LocationRepository(_db);

        Assert.Empty(await locationRepo.GetPathAsync(999));
    }

    [Fact]
    public async Task GetPathStopsAtACycle()
    {
        var locationRepo = new LocationRepository(_db);

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
        var locationRepo = new LocationRepository(_db);

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
        var locationRepo = new LocationRepository(_db);

        var a = await locationRepo.AddAsync(new Location { Name = "A" });
        var b = await locationRepo.AddAsync(new Location { Name = "B", ParentId = a.Id });

        await CreateCycleAsync(a, b);

        Assert.Equal([b.Id], await locationRepo.GetDescendantIdsAsync(a.Id));
    }

    [Fact]
    public async Task UpdateRejectsMovingALocationUnderItsOwnDescendant()
    {
        var locationRepo = new LocationRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf A", ParentId = garage.Id });
        var box = await locationRepo.AddAsync(new Location { Name = "Box 1", ParentId = shelf.Id });

        garage.Name = "Big Garage";
        garage.ParentId = box.Id;
        var refused = await Assert.ThrowsAsync<LocationCycleException>(() => locationRepo.UpdateAsync(garage));
        Assert.Equal((garage.Id, box.Id), (refused.LocationId, refused.ParentId));

        // Nothing of the refused change reaches the database, even after a later save.
        await locationRepo.AddAsync(new Location { Name = "Attic" });

        var stored = await _context.Locations.AsNoTracking().SingleAsync(l => l.Id == garage.Id);
        Assert.Null(stored.ParentId);
        Assert.Equal("Garage", stored.Name);
    }

    [Fact]
    public async Task UpdateRejectsMakingALocationItsOwnParent()
    {
        var locationRepo = new LocationRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });

        garage.ParentId = garage.Id;
        await Assert.ThrowsAsync<LocationCycleException>(() => locationRepo.UpdateAsync(garage));

        await locationRepo.AddAsync(new Location { Name = "Attic" });

        var stored = await _context.Locations.AsNoTracking().SingleAsync(l => l.Id == garage.Id);
        Assert.Null(stored.ParentId);
    }

    [Fact]
    public async Task UpdateAllowsMovingALocationUnderASibling()
    {
        var locationRepo = new LocationRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf A", ParentId = garage.Id });
        var bin = await locationRepo.AddAsync(new Location { Name = "Bin", ParentId = garage.Id });

        bin.ParentId = shelf.Id;
        await locationRepo.UpdateAsync(bin);

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
    }

    [Fact]
    public async Task SetItemPhotosStoresThemInTheGivenOrderWithTheFirstAsPrimary()
    {
        var itemRepo = new ItemRepository(_db);

        var item = await itemRepo.AddAsync(new Item { Name = "Cordless Drill" });

        await itemRepo.SetItemPhotosAsync(item.Id, ["front.jpg", "back.jpg", "serial.jpg"]);

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
        var itemRepo = new ItemRepository(_db);

        var item = await itemRepo.AddAsync(new Item { Name = "Cordless Drill" });
        await itemRepo.SetItemPhotosAsync(item.Id, ["front.jpg", "back.jpg"]);

        // Rewritten so the rows land in the table in the opposite order to the one
        // they should come back in — otherwise insertion order would pass for sorting.
        await itemRepo.SetItemPhotosAsync(item.Id, ["back.jpg", "front.jpg"]);

        var fetched = (await itemRepo.GetAllAsync()).Single();

        Assert.Equal(["back.jpg", "front.jpg"], fetched.Photos.Select(p => p.FileName));
        Assert.Equal("back.jpg", fetched.PrimaryPhotoFileName);
    }

    [Fact]
    public async Task SetItemPhotosReplacesTheWholeSetAndRenumbersIt()
    {
        var itemRepo = new ItemRepository(_db);

        var item = await itemRepo.AddAsync(new Item { Name = "Cordless Drill" });
        await itemRepo.SetItemPhotosAsync(item.Id, ["front.jpg", "back.jpg", "serial.jpg"]);

        // The middle one removed and the survivors swapped.
        await itemRepo.SetItemPhotosAsync(item.Id, ["serial.jpg", "front.jpg"]);

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
        var itemRepo = new ItemRepository(_db);

        var item = await itemRepo.AddAsync(new Item { Name = "Cordless Drill" });

        await itemRepo.SetItemPhotosAsync(item.Id, ["front.jpg", "  ", "front.jpg", "back.jpg", ""]);

        var fetched = await itemRepo.GetByIdAsync(item.Id);

        // A blank names no file, and the same file twice is one photo shown twice.
        Assert.NotNull(fetched);
        Assert.Equal(["front.jpg", "back.jpg"], fetched!.Photos.Select(p => p.FileName));
    }

    [Fact]
    public async Task DeletingAnItemDeletesItsPhotoRows()
    {
        var itemRepo = new ItemRepository(_db);

        var item = await itemRepo.AddAsync(new Item { Name = "Cordless Drill" });
        await itemRepo.SetItemPhotosAsync(item.Id, ["front.jpg", "back.jpg"]);

        await itemRepo.DeleteAsync(item.Id);

        // The files themselves are the caller's to remove; the rows go by cascade,
        // which is what keeps the orphan sweep's "referenced" set honest.
        Assert.Empty(await _context.ItemPhotos.ToListAsync());
    }

    [Fact]
    public async Task ChildSummariesCountOnlyWhatIsDirectlyInside()
    {
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf 2", ParentId = garage.Id });
        var bin = await locationRepo.AddAsync(new Location { Name = "Bin A", ParentId = shelf.Id });
        await locationRepo.AddAsync(new Location { Name = "Bench", ParentId = garage.Id });

        await itemRepo.AddAsync(new Item { Name = "Tyres", LocationId = shelf.Id });
        await itemRepo.AddAsync(new Item { Name = "Pump", LocationId = shelf.Id });
        await itemRepo.AddAsync(new Item { Name = "Cable", LocationId = bin.Id });
        await itemRepo.AddAsync(new Item { Name = "Drill", LocationId = garage.Id });

        var summaries = await locationRepo.GetChildSummariesAsync(garage.Id);

        // The bin's cable is not counted on the shelf, nor the garage's drill on either.
        Assert.Collection(summaries,
            s => Assert.Equal(("Bench", 0), (s.Location.Name, s.ItemCount)),
            s => Assert.Equal(("Shelf 2", 2), (s.Location.Name, s.ItemCount)));
    }

    [Fact]
    public async Task ChildSummariesSortIgnoringCase()
    {
        var locationRepo = new LocationRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        await locationRepo.AddAsync(new Location { Name = "Shelf", ParentId = garage.Id });
        await locationRepo.AddAsync(new Location { Name = "bin", ParentId = garage.Id });
        await locationRepo.AddAsync(new Location { Name = "Ящик", ParentId = garage.Id });
        await locationRepo.AddAsync(new Location { Name = "ящик малий", ParentId = garage.Id });

        var summaries = await locationRepo.GetChildSummariesAsync(garage.Id);

        Assert.Equal(["bin", "Shelf", "Ящик", "ящик малий"], summaries.Select(s => s.Location.Name));
    }

    [Fact]
    public async Task CountInLocationCountsOnlyThatLocation()
    {
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf", ParentId = garage.Id });
        await itemRepo.AddAsync(new Item { Name = "Drill", LocationId = garage.Id });
        await itemRepo.AddAsync(new Item { Name = "Saw", LocationId = garage.Id });
        await itemRepo.AddAsync(new Item { Name = "Tyres", LocationId = shelf.Id });
        await itemRepo.AddAsync(new Item { Name = "Loose" });

        Assert.Equal(2, await itemRepo.CountInLocationAsync(garage.Id));
        Assert.Equal(1, await itemRepo.CountInLocationAsync(shelf.Id));
    }

    [Fact]
    public async Task TopLevelCardsCountThroughEveryLevelBelow()
    {
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf", ParentId = garage.Id });
        var bin = await locationRepo.AddAsync(new Location { Name = "Bin", ParentId = shelf.Id });
        await locationRepo.AddAsync(new Location { Name = "Bench", ParentId = garage.Id });
        var attic = await locationRepo.AddAsync(new Location { Name = "attic" });

        await itemRepo.AddAsync(new Item { Name = "Drill", LocationId = garage.Id });
        await itemRepo.AddAsync(new Item { Name = "Tyres", LocationId = shelf.Id });
        await itemRepo.AddAsync(new Item { Name = "Cable", LocationId = bin.Id });
        await itemRepo.AddAsync(new Item { Name = "Trunk", LocationId = attic.Id });
        await itemRepo.AddAsync(new Item { Name = "Loose" });

        var cards = await locationRepo.GetTopLevelCardsAsync();

        // Top level only, in name order ignoring case.
        Assert.Equal(["attic", "Garage"], cards.Select(c => c.Location.Name));

        var garageCard = cards.Single(c => c.Location.Id == garage.Id);
        Assert.Equal(3, garageCard.ItemCount);
        Assert.Equal(2, garageCard.ChildCount);
        Assert.Equal(1, cards.Single(c => c.Location.Id == attic.Id).ItemCount);
    }

    [Fact]
    public async Task TopLevelCardsTakeTheNewestFourPrimaryPhotosFromBelow()
    {
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf", ParentId = garage.Id });
        var attic = await locationRepo.AddAsync(new Location { Name = "Attic" });

        // AddAsync stamps CreatedAt itself, so "newest" is the order they are added in.
        for (var i = 0; i < 5; i++)
        {
            var item = await itemRepo.AddAsync(new Item
            {
                Name = $"Thing {i}",
                LocationId = i % 2 == 0 ? garage.Id : shelf.Id
            });
            // The second photo is never the cover: the primary is.
            await itemRepo.SetItemPhotosAsync(item.Id, [$"thing{i}.jpg", $"thing{i}-back.jpg"]);
        }
        await itemRepo.AddAsync(new Item { Name = "No photo", LocationId = garage.Id });
        var trunk = await itemRepo.AddAsync(new Item { Name = "Trunk", LocationId = attic.Id });
        await itemRepo.SetItemPhotosAsync(trunk.Id, ["trunk.jpg"]);

        var cards = await locationRepo.GetTopLevelCardsAsync();

        Assert.Equal(
            ["thing4.jpg", "thing3.jpg", "thing2.jpg", "thing1.jpg"],
            cards.Single(c => c.Location.Id == garage.Id).ItemPhotos);
        Assert.Equal(["trunk.jpg"], cards.Single(c => c.Location.Id == attic.Id).ItemPhotos);
    }

    [Fact]
    public async Task SavingUnderADeletedParentIsRefusedAndWritesNothing()
    {
        // What the location form guards against: a parent chosen, then deleted elsewhere
        // before Save. The foreign key refuses the write; the form shows that, not a crash.
        var locationRepo = new LocationRepository(_db);
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf" });
        await locationRepo.DeleteAsync(shelf.Id);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            locationRepo.AddAsync(new Location { Name = "Bin", ParentId = shelf.Id }));
        Assert.False(await _context.Locations.AnyAsync(l => l.Name == "Bin"));
    }

    [Fact]
    public async Task SetQuantityChangesOnlyTheQuantity()
    {
        var itemRepo = new ItemRepository(_db);
        var item = await itemRepo.AddAsync(new Item { Name = "Tyres", Description = "Winter set" });

        Assert.True(await itemRepo.SetQuantityAsync(item.Id, 4));

        var saved = await _context.Items.SingleAsync(i => i.Id == item.Id);
        Assert.Equal((4, "Tyres", "Winter set"), (saved.Quantity, saved.Name, saved.Description));
    }

    [Fact]
    public async Task SetQuantityRefusesZeroAndReportsAMissingItem()
    {
        var itemRepo = new ItemRepository(_db);
        var item = await itemRepo.AddAsync(new Item { Name = "Tyres" });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => itemRepo.SetQuantityAsync(item.Id, 0));
        Assert.False(await itemRepo.SetQuantityAsync(item.Id + 100, 2));
    }

    [Fact]
    public async Task MoveChangesTheLocationAndCanClearIt()
    {
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);
        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var attic = await locationRepo.AddAsync(new Location { Name = "Attic" });
        var item = await itemRepo.AddAsync(new Item { Name = "Tyres", LocationId = garage.Id });

        Assert.True(await itemRepo.MoveAsync(item.Id, attic.Id));
        Assert.Equal("Attic", (await itemRepo.GetByIdAsync(item.Id))!.Location!.Name);

        Assert.True(await itemRepo.MoveAsync(item.Id, null));
        Assert.Null((await itemRepo.GetByIdAsync(item.Id))!.LocationId);

        Assert.False(await itemRepo.MoveAsync(item.Id + 100, attic.Id));
    }

    [Fact]
    public async Task RootLocationsSortLikeTheCards()
    {
        var locationRepo = new LocationRepository(_db);
        await locationRepo.AddAsync(new Location { Name = "Garage" });
        await locationRepo.AddAsync(new Location { Name = "attic" });

        // The search list the Locations tab shows must match the cards it replaces.
        Assert.Equal(
            (await locationRepo.GetTopLevelCardsAsync()).Select(c => c.Location.Name),
            (await locationRepo.GetRootLocationsAsync()).Select(l => l.Name));
    }

    [Fact]
    public async Task RecentItemsAreTheNewestFirst()
    {
        var itemRepo = new ItemRepository(_db);
        for (var i = 0; i < 5; i++)
            await itemRepo.AddAsync(new Item { Name = $"Thing {i}" });

        var recent = await itemRepo.GetRecentAsync(3);

        Assert.Equal(["Thing 4", "Thing 3", "Thing 2"], recent.Select(i => i.Name));
    }

    [Fact]
    public async Task CountsCoverEveryLevel()
    {
        var locationRepo = new LocationRepository(_db);
        var itemRepo = new ItemRepository(_db);

        var garage = await locationRepo.AddAsync(new Location { Name = "Garage" });
        var shelf = await locationRepo.AddAsync(new Location { Name = "Shelf", ParentId = garage.Id });
        await itemRepo.AddAsync(new Item { Name = "Tyres", LocationId = shelf.Id });
        await itemRepo.AddAsync(new Item { Name = "Loose" });

        Assert.Equal(2, await locationRepo.CountAsync());
        Assert.Equal(2, await itemRepo.CountAsync());
    }

    public void Dispose()
    {
        _context.Dispose();
        _db.Dispose();
    }
}
