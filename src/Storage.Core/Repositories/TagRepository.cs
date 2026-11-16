using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;
using Storage.Core.Models;

namespace Storage.Core.Repositories;

// A context per call, for the reasons given on ItemRepository.
public class TagRepository : ITagRepository
{
    private readonly IDbContextFactory<StorageDbContext> _contextFactory;

    public TagRepository(IDbContextFactory<StorageDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<Tag>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Tag>> GetAllWithItemCountsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags
            .Include(t => t.Items)
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<Tag?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags.FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Tag> AddAsync(string name, string color)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var tag = new Tag { Name = name.Trim(), Color = color };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        return tag;
    }

    public async Task UpdateAsync(Tag tag)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Scalars only, onto the stored row: a tag loaded with its items must not drag
        // them into this save.
        var stored = await context.Tags.FindAsync(tag.Id)
            ?? throw new DbUpdateConcurrencyException($"Tag {tag.Id} no longer exists.");

        context.Entry(stored).CurrentValues.SetValues(tag);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var tag = await context.Tags.FindAsync(id);
        if (tag != null)
        {
            context.Tags.Remove(tag);
            await context.SaveChangesAsync();
        }
    }

    public async Task<bool> NameExistsAsync(string name, int? excludingId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var trimmed = name.Trim();

        // Name is NOCASE, so this equality ignores case in SQLite.
        return await context.Tags
            .AnyAsync(t => t.Name == trimmed && (excludingId == null || t.Id != excludingId));
    }

    public async Task SetItemTagsAsync(int itemId, IEnumerable<string> tagNames)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var item = await context.Items
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
        var existing = await context.Tags
            .Where(t => names.Contains(t.Name))
            .ToListAsync();

        // Walks forward as tags are created, so two new tags in one save land on
        // different swatches rather than sharing the count they started from.
        var paletteIndex = await context.Tags.CountAsync();

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
        await context.SaveChangesAsync();
    }
}
