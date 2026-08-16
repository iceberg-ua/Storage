using Storage.Core.Models;

namespace Storage.Core.Repositories;

public interface ITagRepository
{
    Task<IEnumerable<Tag>> GetAllAsync();

    /// <summary>
    /// Tags with their items loaded, so the management screen can show how many items
    /// each one is on.
    /// </summary>
    Task<IEnumerable<Tag>> GetAllWithItemCountsAsync();

    Task<Tag?> GetByIdAsync(int id);

    Task<Tag> AddAsync(string name, string color);

    Task UpdateAsync(Tag tag);

    /// <summary>Deletes the tag. Its rows in the join table cascade away with it.</summary>
    Task DeleteAsync(int id);

    /// <summary>
    /// Case-insensitive check used before a rename or an add, so a clash surfaces as a
    /// message instead of a unique-index violation. <paramref name="excludingId"/> lets
    /// a tag keep its own name when only the colour is changing.
    /// </summary>
    Task<bool> NameExistsAsync(string name, int? excludingId = null);

    /// <summary>
    /// Replaces an item's tags with <paramref name="tagNames"/>, creating any tag that
    /// does not exist yet. Matching is case-insensitive, so an existing tag is reused
    /// rather than duplicated under different casing.
    /// </summary>
    Task SetItemTagsAsync(int itemId, IEnumerable<string> tagNames);
}
