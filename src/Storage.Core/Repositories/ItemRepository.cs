using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;
using Storage.Core.Models;

namespace Storage.Core.Repositories;

// Every call opens its own short-lived context. Pages load in parallel (Home is still
// fetching when the Items tab asks), and a DbContext allows one operation at a time —
// one per call also means no stale cached entities and no failed change left pending.
public class ItemRepository : IItemRepository
{
    private readonly IDbContextFactory<StorageDbContext> _contextFactory;

    public ItemRepository(IDbContextFactory<StorageDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<Item>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Items
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
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Items
            .Include(i => i.Location)
            .Include(i => i.Tags)
            .Include(i => i.Photos.OrderBy(p => p.SortOrder))
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<IEnumerable<Item>> GetByLocationIdAsync(int locationId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Items
            .Include(i => i.Location)
            .Include(i => i.Tags)
            .Include(i => i.Photos.OrderBy(p => p.SortOrder))
            .Where(i => i.LocationId == locationId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<int> CountInLocationAsync(int locationId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Items.CountAsync(i => i.LocationId == locationId);
    }

    public async Task<int> CountAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Items.CountAsync();
    }

    public async Task<IReadOnlyList<Item>> GetRecentAsync(int count)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Items
            .Include(i => i.Location)
            .Include(i => i.Photos.OrderBy(p => p.SortOrder))
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Take(count)
            .ToListAsync();
    }

    public async Task<IEnumerable<Item>> SearchAsync(string? query, SearchScope scope, int? locationId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Same shape as GetAllAsync, so a search result row renders exactly like a
        // browsed one — thumbnail, location and tag chips all present.
        var items = context.Items
            .Include(i => i.Location)
            .Include(i => i.Tags)
            .Include(i => i.Photos.OrderBy(p => p.SortOrder))
            .AsQueryable();

        if (locationId is int id)
            items = items.Where(i => i.LocationId == id);

        var term = query?.Trim();

        // An empty box is not "match nothing", it is "no filter at all".
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = LikePattern.Contains(term);

            // UnicodeLower on both sides: the column is folded per row by the SQLite
            // function, the term is already folded here by the same .NET rules.
            items = scope switch
            {
                SearchScope.Name => items.Where(i =>
                    EF.Functions.Like(StorageDbContext.UnicodeLower(i.Name)!, pattern, LikePattern.Escape)),

                SearchScope.Description => items.Where(i =>
                    i.Description != null &&
                    EF.Functions.Like(StorageDbContext.UnicodeLower(i.Description)!, pattern, LikePattern.Escape)),

                SearchScope.Tags => items.Where(i =>
                    i.Tags.Any(t => EF.Functions.Like(StorageDbContext.UnicodeLower(t.Name)!, pattern, LikePattern.Escape))),

                // One OR'd predicate rather than three unioned queries, so an item
                // matching on both its name and a tag still comes back exactly once.
                _ => items.Where(i =>
                    EF.Functions.Like(StorageDbContext.UnicodeLower(i.Name)!, pattern, LikePattern.Escape) ||
                    (i.Description != null &&
                     EF.Functions.Like(StorageDbContext.UnicodeLower(i.Description)!, pattern, LikePattern.Escape)) ||
                    i.Tags.Any(t => EF.Functions.Like(StorageDbContext.UnicodeLower(t.Name)!, pattern, LikePattern.Escape)))
            };
        }

        return await items
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<Item> AddAsync(Item item)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        item.CreatedAt = DateTime.UtcNow;

        // Attach, not Add: a location or tags the caller loaded through another call
        // carry their ids and stay as they are, while anything without an id yet — the
        // item, a new tag, its photos — is inserted. Add would insert them all.
        context.Items.Attach(item);
        await context.SaveChangesAsync();
        return item;
    }

    public async Task UpdateAsync(Item item)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // The caller's item came from another, already disposed context, often with its
        // location, tags and photos attached. Copying only the scalar values onto the
        // stored row leaves that graph alone — those writes belong to
        // ITagRepository.SetItemTagsAsync and SetItemPhotosAsync.
        var stored = await context.Items.FindAsync(item.Id)
            ?? throw new DbUpdateConcurrencyException($"Item {item.Id} no longer exists.");

        context.Entry(stored).CurrentValues.SetValues(item);
        await context.SaveChangesAsync();
    }

    public async Task<bool> SetQuantityAsync(int itemId, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);

        // One UPDATE in a context of its own: no entity to load, and nothing left behind
        // in a shared tracker if it fails.
        await using var context = await _contextFactory.CreateDbContextAsync();
        var updated = await context.Items
            .Where(i => i.Id == itemId)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.Quantity, quantity));

        return updated > 0;
    }

    public async Task<bool> MoveAsync(int itemId, int? locationId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var updated = await context.Items
            .Where(i => i.Id == itemId)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.LocationId, locationId));

        return updated > 0;
    }

    public async Task SetItemPhotosAsync(int itemId, IEnumerable<string> fileNames)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var item = await context.Items
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

        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var item = await context.Items.FindAsync(id);
        if (item != null)
        {
            // The ItemPhotos rows go with it by cascade; the files they name are the
            // caller's to remove, since only it knows the write succeeded.
            context.Items.Remove(item);
            await context.SaveChangesAsync();
        }
    }
}
