using Storage.Core.Models;

namespace Storage.Core.Repositories;

public interface ILocationRepository
{
    Task<IEnumerable<Location>> GetAllAsync();
    Task<Location?> GetByIdAsync(int id);
    Task<IEnumerable<Location>> GetRootLocationsAsync();
    Task<IEnumerable<Location>> GetChildrenAsync(int parentId);
    Task<Location> AddAsync(Location location);
    Task UpdateAsync(Location location);
    Task DeleteAsync(int id);
}
