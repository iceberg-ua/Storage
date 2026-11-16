using Storage.Core.Models;

namespace Storage.Core.Repositories;

public interface IItemRepository
{
    Task<IEnumerable<Item>> GetAllAsync();
    Task<Item?> GetByIdAsync(int id);
    Task<IEnumerable<Item>> GetByLocationIdAsync(int locationId);

    /// <summary>
    /// How many items are stored in <paramref name="locationId"/> itself, not in the
    /// locations nested under it.
    /// </summary>
    Task<int> CountInLocationAsync(int locationId);

    /// <summary>How many items there are, wherever they are stored.</summary>
    Task<int> CountAsync();

    /// <summary>
    /// The <paramref name="count"/> most recently added items, newest first, with their
    /// location and photos.
    /// </summary>
    Task<IReadOnlyList<Item>> GetRecentAsync(int count);

    /// <summary>
    /// Items whose <paramref name="scope"/> field contains <paramref name="query"/>,
    /// matched case-insensitively in any language and partially. A blank query is not
    /// an error — it means "no filter", so the caller gets the same set it would from
    /// <see cref="GetAllAsync"/>. Pass <paramref name="locationId"/> to search within a
    /// single location, or null to search the whole inventory.
    /// </summary>
    Task<IEnumerable<Item>> SearchAsync(string? query, SearchScope scope, int? locationId = null);

    Task<Item> AddAsync(Item item);
    Task UpdateAsync(Item item);

    /// <summary>
    /// Replaces the item's photos with <paramref name="fileNames"/>, in the order
    /// given — the first becomes the primary. Files on disk are not touched; that is
    /// the caller's job, once the write has committed.
    /// </summary>
    Task SetItemPhotosAsync(int itemId, IEnumerable<string> fileNames);

    Task DeleteAsync(int id);
}
