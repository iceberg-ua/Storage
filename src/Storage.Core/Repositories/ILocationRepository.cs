using Storage.Core.Models;

namespace Storage.Core.Repositories;

public interface ILocationRepository
{
    Task<IEnumerable<Location>> GetAllAsync();
    Task<Location?> GetByIdAsync(int id);
    Task<IEnumerable<Location>> GetRootLocationsAsync();
    Task<IEnumerable<Location>> GetChildrenAsync(int parentId);

    /// <summary>
    /// The chain from the top-level location down to <paramref name="id"/>, inclusive,
    /// root first. Empty when the location does not exist.
    /// </summary>
    Task<IReadOnlyList<Location>> GetPathAsync(int id);

    /// <summary>
    /// Ids of every location nested under <paramref name="id"/>, at any depth, not
    /// including <paramref name="id"/> itself. These are the parents a location can't
    /// be moved under.
    /// </summary>
    Task<IReadOnlySet<int>> GetDescendantIdsAsync(int id);

    /// <summary>
    /// Locations whose name contains <paramref name="query"/>, matched
    /// case-insensitively in any language and partially. A blank query means "no
    /// filter". Pass <paramref name="parentId"/> to search only that location's
    /// children, or null to search every location.
    /// </summary>
    Task<IEnumerable<Location>> SearchAsync(string? query, int? parentId = null);

    Task<Location> AddAsync(Location location);
    /// <exception cref="InvalidOperationException">
    /// The location's parent is itself or one of its own descendants. Nothing is
    /// saved, and a tracked <paramref name="location"/> is reset to its loaded values.
    /// </exception>
    Task UpdateAsync(Location location);
    Task DeleteAsync(int id);
}
