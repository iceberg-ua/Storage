using Storage.Core.Models;

namespace Storage.Core.Repositories;

public interface IItemRepository
{
    Task<IEnumerable<Item>> GetAllAsync();
    Task<Item?> GetByIdAsync(int id);
    Task<IEnumerable<Item>> GetByLocationIdAsync(int locationId);
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
