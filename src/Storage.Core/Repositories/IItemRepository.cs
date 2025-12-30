using Storage.Core.Models;

namespace Storage.Core.Repositories;

public interface IItemRepository
{
    Task<IEnumerable<Item>> GetAllAsync();
    Task<Item?> GetByIdAsync(int id);
    Task<IEnumerable<Item>> GetByLocationIdAsync(int locationId);
    Task<Item> AddAsync(Item item);
    Task UpdateAsync(Item item);
    Task DeleteAsync(int id);
}
