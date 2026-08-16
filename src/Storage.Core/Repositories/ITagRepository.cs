using Storage.Core.Models;

namespace Storage.Core.Repositories;

public interface ITagRepository
{
    Task<IEnumerable<Tag>> GetAllAsync();

    /// <summary>
    /// Replaces an item's tags with <paramref name="tagNames"/>, creating any tag that
    /// does not exist yet. Matching is case-insensitive, so an existing tag is reused
    /// rather than duplicated under different casing.
    /// </summary>
    Task SetItemTagsAsync(int itemId, IEnumerable<string> tagNames);
}
