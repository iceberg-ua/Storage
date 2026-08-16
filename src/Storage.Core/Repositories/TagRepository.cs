using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;
using Storage.Core.Models;

namespace Storage.Core.Repositories;

public class TagRepository : ITagRepository
{
    private readonly StorageDbContext _context;

    public TagRepository(StorageDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Tag>> GetAllAsync()
    {
        return await _context.Tags
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Tag>> GetAllWithItemCountsAsync()
    {
        return await _context.Tags
            .Include(t => t.Items)
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<Tag?> GetByIdAsync(int id)
    {
        return await _context.Tags.FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Tag> AddAsync(string name, string color)
    {
        var tag = new Tag { Name = name.Trim(), Color = color };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();
        return tag;
    }

    public async Task UpdateAsync(Tag tag)
    {
        _context.Entry(tag).State = EntityState.Modified;
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var tag = await _context.Tags.FindAsync(id);
        if (tag != null)
        {
            _context.Tags.Remove(tag);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> NameExistsAsync(string name, int? excludingId = null)
    {
        var trimmed = name.Trim();

        // Name is NOCASE, so this equality ignores case in SQLite.
        return await _context.Tags
            .AnyAsync(t => t.Name == trimmed && (excludingId == null || t.Id != excludingId));
    }

    public async Task SetItemTagsAsync(int itemId, IEnumerable<string> tagNames)
    {
        var item = await _context.Items
            .Include(i => i.Tags)
            .FirstOrDefaultAsync(i => i.Id == itemId);

        if (item is null)
            return;

        // Collapse case-variants here too, so one item can't be handed both
        // "Tools" and "tools" and end up asking for the same row twice.
        var names = tagNames
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .DistinctBy(n => n.ToLowerInvariant())
            .ToList();

        // One query for the whole set. The Name column is NOCASE, so this IN
        // comparison matches regardless of how the caller capitalised things.
        var existing = await _context.Tags
            .Where(t => names.Contains(t.Name))
            .ToListAsync();

        // Walks forward as tags are created, so two new tags in one save land on
        // different swatches rather than sharing the count they started from.
        var paletteIndex = await _context.Tags.CountAsync();

        item.Tags.Clear();

        foreach (var name in names)
        {
            var tag = existing.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

            if (tag is null)
            {
                tag = new Tag { Name = name, Color = TagPalette.ForIndex(paletteIndex) };
                paletteIndex++;
            }

            item.Tags.Add(tag);
        }

        // Tags no longer on any item are left in place — they stay available for
        // reuse and in autocomplete. Removing them is the Tags screen's job.
        await _context.SaveChangesAsync();
    }
}
