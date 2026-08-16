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

        item.Tags.Clear();

        foreach (var name in names)
        {
            var tag = existing.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? new Tag { Name = name };

            item.Tags.Add(tag);
        }

        // Tags no longer on any item are left in place — they stay available for
        // reuse and in autocomplete. Removing them globally is tag management.
        await _context.SaveChangesAsync();
    }
}
