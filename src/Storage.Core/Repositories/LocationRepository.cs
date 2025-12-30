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
