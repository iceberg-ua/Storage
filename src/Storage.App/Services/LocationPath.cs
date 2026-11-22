using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.Services;

/// <summary>"Garage › Shelf 2": a location and everything above it, from locations already loaded.</summary>
public static class LocationPath
{
    /// <summary>
    /// The chain from the top down to <paramref name="locationId"/>, inclusive; empty for
    /// null or an id not in <paramref name="byId"/>. Stops at a repeat, as the
    /// repositories do, in case an old cycle is still in the database.
    /// </summary>
    public static List<StorageLocation> Chain(int? locationId, IReadOnlyDictionary<int, StorageLocation> byId)
    {
        var chain = new List<StorageLocation>();
        var seen = new HashSet<int>();
        while (locationId is int id && seen.Add(id) && byId.TryGetValue(id, out var location))
        {
            chain.Add(location);
            locationId = location.ParentId;
        }

        chain.Reverse();
        return chain;
    }

    public static string Of(int? locationId, IReadOnlyDictionary<int, StorageLocation> byId) =>
        string.Join(" › ", Chain(locationId, byId).Select(l => l.Name));
}
