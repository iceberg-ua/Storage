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
            .Include(i => i.Tags)
            // Ordered in the include: the first photo is the primary, so the order is
            // part of the data, not a detail every caller has to remember to apply.
            .Include(i => i.Photos.OrderBy(p => p.SortOrder))
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<Item?> GetByIdAsync(int id)
    {
        return await _context.Items
            .Include(i => i.Location)
            .Include(i => i.Tags)
            .Include(i => i.Photos.OrderBy(p => p.SortOrder))
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<IEnumerable<Item>> GetByLocationIdAsync(int locationId)
    {
        return await _context.Items
            .Include(i => i.Location)
            .Include(i => i.Tags)
            .Include(i => i.Photos.OrderBy(p => p.SortOrder))
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
        // Marks this row and nothing else. Update() walks the graph, so now that items
        // carry tags and photos it would mark those rows modified too — those writes
        // belong to ITagRepository.SetItemTagsAsync and SetItemPhotosAsync.
        _context.Entry(item).State = EntityState.Modified;
        await _context.SaveChangesAsync();
    }

    public async Task SetItemPhotosAsync(int itemId, IEnumerable<string> fileNames)
    {
        var item = await _context.Items
            .Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == itemId);

        if (item is null)
            return;

        var names = fileNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Rewritten wholesale rather than diffed. The set is capped at a handful and
        // the incoming order *is* the order, so reassigning every SortOrder is both
        // simpler and less prone to leaving a gap than reconciling positions. The
        // relationship is required and cascading, so the cleared rows are deleted.
        item.Photos.Clear();

        for (var i = 0; i < names.Count; i++)
            item.Photos.Add(new ItemPhoto { FileName = names[i], SortOrder = i });

        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var item = await _context.Items.FindAsync(id);
        if (item != null)
        {
            // The ItemPhotos rows go with it by cascade; the files they name are the
            // caller's to remove, since only it knows the write succeeded.
            _context.Items.Remove(item);
            await _context.SaveChangesAsync();
        }
    }
}
