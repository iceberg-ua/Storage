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

    public async Task<Location> AddAsync(Location location)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Locations.Add(location);
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

    public async Task<Location?> DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var location = await context.Locations.FindAsync(id);
        if (location != null)
        {
            // A location with children is Restrict, so SQLite refuses this delete. The
            // context goes with the call, so the refusal can't be retried by a later save.
            context.Locations.Remove(location);
            await context.SaveChangesAsync();
        }

        return location;
    }
}
