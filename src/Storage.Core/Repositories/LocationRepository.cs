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

        // The edit form only stops a location from being its own parent, not from
        // being its grandparent's, so a cycle is possible — stop at the first repeat
        // rather than walking forever.
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
