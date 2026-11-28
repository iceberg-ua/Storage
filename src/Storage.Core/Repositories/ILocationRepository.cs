using Storage.Core.Models;

namespace Storage.Core.Repositories;

public interface ILocationRepository
{
    Task<IEnumerable<Location>> GetAllAsync();
    Task<Location?> GetByIdAsync(int id);
    Task<IEnumerable<Location>> GetRootLocationsAsync();
    Task<IEnumerable<Location>> GetChildrenAsync(int parentId);

    /// <summary>
    /// The locations directly under <paramref name="parentId"/>, each with its item
    /// count, in name order ignoring case.
    /// </summary>
    Task<IReadOnlyList<LocationSummary>> GetChildSummariesAsync(int parentId);

    /// <summary>
    /// Every top-level location as a <see cref="PlaceCard"/>, in name order ignoring case.
    /// </summary>
    Task<IReadOnlyList<PlaceCard>> GetTopLevelCardsAsync();

    /// <summary>How many locations there are, at every level.</summary>
    Task<int> CountAsync();

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
    /// Locations whose name or description contains <paramref name="query"/>, matched
    /// case-insensitively in any language and partially. A blank query means "no
    /// filter". Pass <paramref name="parentId"/> to search only that location's
    /// children, or null to search every location.
    /// </summary>
    Task<IEnumerable<Location>> SearchAsync(string? query, int? parentId = null);

    /// <summary>
    /// Locations whose name or description contains <paramref name="text"/>, matched as
    /// <see cref="SearchAsync"/> does, anywhere under <paramref name="withinLocationId"/>
    /// (not counting it) or anywhere at all when that is null. Name order, ignoring case.
    /// </summary>
    Task<IReadOnlyList<Location>> FindAsync(string? text, int? withinLocationId);

    Task<Location> AddAsync(Location location);
    /// <exception cref="LocationCycleException">
    /// The location's parent is itself or one of its own descendants. Nothing is saved.
    /// </exception>
    Task UpdateAsync(Location location);
    /// <summary>
    /// What is inside the location, for the delete confirmation to describe. Null when
    /// there is no such location.
    /// </summary>
    Task<LocationContents?> GetContentsAsync(int id);

    /// <summary>
    /// Deletes the location, in one transaction. With <paramref name="deleteContents"/>
    /// every location nested under it and every item in any of them go too. Without it,
    /// its sub-locations and items move to its parent (or to the top level and to no
    /// location), and everything further down moves with them.
    /// </summary>
    /// <returns>
    /// The photo files of every location and item deleted, for the caller to remove once
    /// it knows the rows are gone; null when there was no such location.
    /// </returns>
    Task<IReadOnlyList<string>?> DeleteAsync(int id, bool deleteContents = false);
}
