using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;
using Storage.Core.Models;

namespace Storage.Core.Repositories;

// A context per call, for the reasons given on ItemRepository.
public class LocationRepository : ILocationRepository
{
    private readonly IDbContextFactory<StorageDbContext> _contextFactory;

    public LocationRepository(IDbContextFactory<StorageDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<Location>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations
            .Include(l => l.Parent)
            .OrderBy(l => l.Name)
            .ToListAsync();
    }

    public async Task<Location?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations
            .Include(l => l.Parent)
            .Include(l => l.Children)
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<IEnumerable<Location>> GetRootLocationsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations
            .Where(l => l.ParentId == null)
            // Folded, like the cards and chips, so every list of locations agrees on
            // where "attic" goes relative to "Garage".
            .OrderBy(l => StorageDbContext.UnicodeLower(l.Name))
            .ToListAsync();
    }

    public async Task<IEnumerable<Location>> GetChildrenAsync(int parentId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations
            .Where(l => l.ParentId == parentId)
            .OrderBy(l => StorageDbContext.UnicodeLower(l.Name))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<LocationSummary>> GetChildSummariesAsync(int parentId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Counted in the query, so a shelf of a hundred items costs one number, not a
        // hundred loaded rows. Ordered on the folded name: SQLite's default collation
        // would put "bin" after "Shelf".
        var rows = await context.Locations
            .AsNoTracking()
            .Where(l => l.ParentId == parentId)
            .OrderBy(l => StorageDbContext.UnicodeLower(l.Name))
            .Select(l => new { Location = l, Items = l.Items.Count })
            .ToListAsync();

        return rows.Select(r => new LocationSummary(r.Location, r.Items)).ToList();
    }

    public async Task<IReadOnlyList<PlaceCard>> GetTopLevelCardsAsync()
    {
        const int photosPerCard = 4;

        await using var context = await _contextFactory.CreateDbContextAsync();

        // Three flat queries, put together in memory: the whole tree, item counts per
        // location, and the primary photo of every item that has one. A card counts
        // through every level below it, which one SQL query per card would make slow,
        // and the app holds a few hundred locations at most.
        var locations = await context.Locations
            .AsNoTracking()
            .OrderBy(l => StorageDbContext.UnicodeLower(l.Name))
            .ToListAsync();

        var itemCounts = await context.Items
            .Where(i => i.LocationId != null)
            .GroupBy(i => i.LocationId!.Value)
            .Select(g => new { LocationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.LocationId, g => g.Count);

        var photos = await context.Items
            .Where(i => i.LocationId != null && i.Photos.Any())
            // Id breaks ties: two items saved in the same tick keep the order they were added.
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Select(i => new
            {
                LocationId = i.LocationId!.Value,
                FileName = i.Photos.OrderBy(p => p.SortOrder).Select(p => p.FileName).First()
            })
            .ToListAsync();

        var children = locations.ToLookup(l => l.ParentId);
        var roots = children[null].ToList();

        // Which card each location counts towards, so the counts and the photos are each
        // one pass rather than one pass per card.
        var rootOf = new Dictionary<int, int>();
        foreach (var root in roots)
        {
            foreach (var id in Subtree(root.Id, children))
                rootOf.TryAdd(id, root.Id);
        }

        var totals = new Dictionary<int, int>();
        foreach (var (locationId, count) in itemCounts)
        {
            if (rootOf.TryGetValue(locationId, out var rootId))
                totals[rootId] = totals.GetValueOrDefault(rootId) + count;
        }

        // Already newest first, so each card keeps the first ones it is handed.
        var covers = roots.ToDictionary(r => r.Id, _ => new List<string>(photosPerCard));
        foreach (var photo in photos)
        {
            if (rootOf.TryGetValue(photo.LocationId, out var rootId) && covers[rootId].Count < photosPerCard)
                covers[rootId].Add(photo.FileName);
        }

        return roots
            .Select(r => new PlaceCard(r, totals.GetValueOrDefault(r.Id), children[r.Id].Count(), covers[r.Id]))
            .ToList();
    }

    // The location and everything under it. Stops at a repeat, like GetPathAsync, in
    // case a cycle written before saves rejected them is still in the database.
    private static HashSet<int> Subtree(int rootId, ILookup<int?, Location> children)
    {
        var seen = new HashSet<int>();
        var pending = new Stack<int>([rootId]);
        while (pending.TryPop(out var id))
        {
            if (!seen.Add(id))
                continue;

            foreach (var child in children[id])
                pending.Push(child.Id);
        }

        return seen;
    }

    public async Task<int> CountAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations.CountAsync();
    }

    public async Task<IReadOnlyList<Location>> GetPathAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var path = new List<Location>();
        var visited = new HashSet<int>();
        int? next = id;

        // Saves now reject a cycle, but ones written before that check may still be in
        // the database — stop at the first repeat rather than walking forever.
        while (next is int current && visited.Add(current))
        {
            var location = await context.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == current);
            if (location is null)
                break;

            path.Add(location);
            next = location.ParentId;
        }

        path.Reverse();
        return path;
    }

    public async Task<IReadOnlySet<int>> GetDescendantIdsAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // One query for the whole tree, walked in memory: the app holds a few hundred
        // locations at most, and this avoids a round trip per level.
        var links = await context.Locations
            .AsNoTracking()
            .Where(l => l.ParentId != null)
            .Select(l => new { l.Id, ParentId = l.ParentId!.Value })
            .ToListAsync();

        var childrenOf = links.ToLookup(l => l.ParentId, l => l.Id);

        var descendants = new HashSet<int>();
        var pending = new Stack<int>([id]);

        // A cycle saved before the update check existed would loop back here, so the
        // set doubles as the visited list.
        while (pending.TryPop(out var current))
        {
            foreach (var child in childrenOf[current])
            {
                if (child != id && descendants.Add(child))
                    pending.Push(child);
            }
        }

        return descendants;
    }

    public async Task<IEnumerable<Location>> SearchAsync(string? query, int? parentId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var locations = context.Locations
            .Include(l => l.Parent)
            .AsQueryable();

        if (parentId is int id)
            locations = locations.Where(l => l.ParentId == id);

        var term = query?.Trim();

        if (!string.IsNullOrEmpty(term))
        {
            // A location has only a name and a description, both always searched, so
            // there is nothing here for the item search's field scope — that strip is
            // hidden while this runs.
            var pattern = LikePattern.Contains(term);

            locations = locations.Where(l =>
                EF.Functions.Like(StorageDbContext.UnicodeLower(l.Name)!, pattern, LikePattern.Escape) ||
                EF.Functions.Like(StorageDbContext.UnicodeLower(l.Description)!, pattern, LikePattern.Escape));
        }

        return await locations
            .OrderBy(l => l.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Location>> FindAsync(string? text, int? withinLocationId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        IQueryable<Location> locations = context.Locations.AsNoTracking();

        if (withinLocationId is int root)
        {
            var within = await LocationTree.SubtreeIdsAsync(context, root);
            within.Remove(root);
            locations = locations.Where(l => within.Contains(l.Id));
        }

        var term = text?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = LikePattern.Contains(term);
            locations = locations.Where(l =>
                EF.Functions.Like(StorageDbContext.UnicodeLower(l.Name)!, pattern, LikePattern.Escape) ||
                EF.Functions.Like(StorageDbContext.UnicodeLower(l.Description)!, pattern, LikePattern.Escape));
        }

        return await locations
            .OrderBy(l => StorageDbContext.UnicodeLower(l.Name))
            .ToListAsync();
    }

    public async Task<Location> AddAsync(Location location)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Attach, not Add, as in ItemRepository.AddAsync: a parent loaded through another
        // call keeps its row instead of being inserted a second time.
        context.Locations.Attach(location);
        await context.SaveChangesAsync();
        return location;
    }

    public async Task UpdateAsync(Location location)
    {
        if (location.ParentId is int parentId &&
            (parentId == location.Id || (await GetDescendantIdsAsync(location.Id)).Contains(parentId)))
        {
            throw new LocationCycleException(location.Id, parentId);
        }

        await using var context = await _contextFactory.CreateDbContextAsync();

        // The caller's location usually arrives with its parent, children and items
        // attached. Update() would walk that graph and write every row in it; copying the
        // scalars onto the stored row writes this one only.
        var stored = await context.Locations.FindAsync(location.Id)
            ?? throw new DbUpdateConcurrencyException($"Location {location.Id} no longer exists.");

        context.Entry(stored).CurrentValues.SetValues(location);
        await context.SaveChangesAsync();
    }

    public async Task<LocationContents?> GetContentsAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var location = await context.Locations
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new { l.ParentId, ParentName = l.Parent == null ? null : l.Parent.Name })
            .FirstOrDefaultAsync();
        if (location is null)
            return null;

        var subtree = await LocationTree.SubtreeIdsAsync(context, id);

        // A parent inside its own subtree (a cycle from before saves rejected them) is
        // where DeleteAsync won't move things, so the dialog mustn't name it either.
        var parentName = location.ParentId is int parentId && subtree.Contains(parentId) ? null : location.ParentName;
        var items = await context.Items
            .CountAsync(i => i.LocationId != null && subtree.Contains(i.LocationId.Value));
        var directItems = await context.Items.CountAsync(i => i.LocationId == id);

        return new LocationContents(subtree.Count - 1, items, directItems, parentName);
    }

    // Set-based updates and deletes rather than Remove: what EF would do to children it
    // happens to have loaded is exactly what made this delete depend on browsing history.
    public async Task<IReadOnlyList<string>?> DeleteAsync(int id, bool deleteContents = false)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var location = await context.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id);
        if (location is null)
            return null;

        var subtree = await LocationTree.SubtreeIdsAsync(context, id);
        // Deleting the contents takes the location with them; moving them up leaves it
        // empty, so the delete below always finds nothing in the way.
        var photos = deleteContents
            ? await DeleteContentsAsync(context, subtree)
            : await MoveContentsUpAsync(context, location, subtree);

        await context.Locations.Where(l => l.Id == id).ExecuteDeleteAsync();
        await transaction.CommitAsync();
        return photos;
    }

    // Returns the photo files of every row it deletes, the location's own included.
    private static async Task<IReadOnlyList<string>> DeleteContentsAsync(StorageDbContext context, HashSet<int> subtree)
    {
        var itemPhotos = await context.ItemPhotos
            .Where(p => p.Item!.LocationId != null && subtree.Contains(p.Item.LocationId.Value))
            .Select(p => p.FileName)
            .ToListAsync();
        var locationPhotos = await context.Locations
            .Where(l => subtree.Contains(l.Id) && l.PhotoFileName != null)
            .Select(l => l.PhotoFileName!)
            .ToListAsync();

        // The items' photo and tag rows go with them, by cascade.
        await context.Items
            .Where(i => i.LocationId != null && subtree.Contains(i.LocationId.Value))
            .ExecuteDeleteAsync();

        // A parent is Restrict, and SQLite checks that row by row, so one DELETE of the
        // whole tree could hit a parent before its child. Unlinked first, they go in any order.
        await context.Locations
            .Where(l => subtree.Contains(l.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ParentId, (int?)null));
        await context.Locations
            .Where(l => subtree.Contains(l.Id))
            .ExecuteDeleteAsync();

        return [.. locationPhotos, .. itemPhotos];
    }

    // Only the location itself is deleted, so only its own photo is returned.
    private static async Task<IReadOnlyList<string>> MoveContentsUpAsync(
        StorageDbContext context, Location location, HashSet<int> subtree)
    {
        // A cycle saved before saves rejected them could make the parent one of its own
        // descendants; moving the children there would leave them pointing at themselves.
        var target = location.ParentId is int parentId && !subtree.Contains(parentId) ? location.ParentId : null;

        await context.Locations
            .Where(l => l.ParentId == location.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ParentId, target));
        await context.Items
            .Where(i => i.LocationId == location.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.LocationId, target));

        return location.PhotoFileName is string photo ? [photo] : [];
    }
}
