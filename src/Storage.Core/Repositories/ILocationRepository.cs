using Storage.Core.Models;

namespace Storage.Core.Repositories;

public interface ILocationRepository
{
    Task<IEnumerable<Location>> GetAllAsync();
    Task<Location?> GetByIdAsync(int id);
    Task<IEnumerable<Location>> GetRootLocationsAsync();
    Task<IEnumerable<Location>> GetChildrenAsync(int parentId);

    /// <summary>
    /// Locations whose name contains <paramref name="query"/>, matched
    /// case-insensitively in any language and partially. A blank query means "no
    /// filter". Pass <paramref name="parentId"/> to search only that location's
    /// children, or null to search every location.
    /// </summary>
    Task<IEnumerable<Location>> SearchAsync(string? query, int? parentId = null);

    Task<Location> AddAsync(Location location);
    Task UpdateAsync(Location location);
    Task DeleteAsync(int id);
}
