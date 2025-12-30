using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;
using Storage.Core.Models;

namespace Storage.Core.Repositories;

public class ItemRepository : IItemRepository
{
    private readonly StorageDbContext _context;

    public ItemRepository(StorageDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Item>> GetAllAsync()
    {
        return await _context.Items
            .Include(i => i.Location)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<Item?> GetByIdAsync(int id)
    {
        return await _context.Items
            .Include(i => i.Location)
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<IEnumerable<Item>> GetByLocationIdAsync(int locationId)
    {
        return await _context.Items
            .Include(i => i.Location)
            .Where(i => i.LocationId == locationId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<Item> AddAsync(Item item)
    {
        item.CreatedAt = DateTime.UtcNow;
        _context.Items.Add(item);
        await _context.SaveChangesAsync();
        return item;
    }

    public async Task UpdateAsync(Item item)
    {
        _context.Items.Update(item);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var item = await _context.Items.FindAsync(id);
        if (item != null)
        {
            _context.Items.Remove(item);
            await _context.SaveChangesAsync();
        }
    }
}
