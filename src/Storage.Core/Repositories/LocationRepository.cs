using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;
using Storage.Core.Models;

namespace Storage.Core.Repositories;

public class LocationRepository : ILocationRepository
{
    private readonly StorageDbContext _context;

    public LocationRepository(StorageDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Location>> GetAllAsync()
    {
        return await _context.Locations
            .Include(l => l.Parent)
            .OrderBy(l => l.Name)
            .ToListAsync();
    }

    public async Task<Location?> GetByIdAsync(int id)
    {
        return await _context.Locations
            .Include(l => l.Parent)
            .Include(l => l.Children)
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<IEnumerable<Location>> GetRootLocationsAsync()
    {
        return await _context.Locations
            .Where(l => l.ParentId == null)
            .OrderBy(l => l.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Location>> GetChildrenAsync(int parentId)
    {
        return await _context.Locations
            .Where(l => l.ParentId == parentId)
            .OrderBy(l => l.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Location>> GetPathAsync(int id)
    {
        var path = new List<Location>();
        var visited = new HashSet<int>();
        int? next = id;

        // Saves now reject a cycle, but ones written before that check may still be in
        // the database — stop at the first repeat rather than walking forever.
        while (next is int current && visited.Add(current))
        {
            // Untracked: the caller may hold this repository for the app's lifetime, and
            // a tracked query would hand back the instance cached on the first read,
            // missing a rename or move saved through another context since.
            var location = await _context.Locations
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
        // One query for the whole tree, walked in memory: the app holds a few hundred
        // locations at most, and this avoids a round trip per level.
        var links = await _context.Locations
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
        var locations = _context.Locations
            .Include(l => l.Parent)
            .AsQueryable();

        if (parentId is int id)
            locations = locations.Where(l => l.ParentId == id);

        var term = query?.Trim();

        if (!string.IsNullOrEmpty(term))
        {
            // A location has only a name, so there is nothing here matching the item
            // search's field scope — that strip is hidden while this runs.
            var pattern = LikePattern.Contains(term);

            locations = locations.Where(l =>
                EF.Functions.Like(StorageDbContext.UnicodeLower(l.Name)!, pattern, LikePattern.Escape));
        }

        return await locations
            .OrderBy(l => l.Name)
            .ToListAsync();
    }

    public async Task<Location> AddAsync(Location location)
    {
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();
        return location;
    }

    public async Task UpdateAsync(Location location)
    {
        if (location.ParentId is int parentId &&
            (parentId == location.Id || (await GetDescendantIdsAsync(location.Id)).Contains(parentId)))
        {
            throw new InvalidOperationException(
                $"Location {location.Id} can't be moved under itself or one of its own descendants.");
        }

        _context.Locations.Update(location);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var location = await _context.Locations.FindAsync(id);
        if (location != null)
        {
            _context.Locations.Remove(location);
            await _context.SaveChangesAsync();
        }
    }
}
