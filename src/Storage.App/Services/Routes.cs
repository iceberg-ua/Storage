using Storage.App.ViewModels;

namespace Storage.App.Services;

/// <summary>
/// Builds the Shell routes whose query keys a page reads back, so a key is spelled in
/// one place. A misspelt key fails silently: the page just opens without the value.
/// </summary>
public static class Routes
{
    /// <summary>
    /// The create page, opening on <paramref name="kind"/>. A <paramref name="locationId"/>
    /// becomes the new item's location or the new location's parent.
    /// </summary>
    /// <remarks>Read back by <see cref="CreateViewModel.ApplyQueryAttributes"/>.</remarks>
    public static string Create(CreateKind kind, int? locationId = null)
    {
        var route = $"create?kind={kind.ToString().ToLowerInvariant()}";
        return locationId is int id ? $"{route}&locationId={id}" : route;
    }

    /// <summary>One location's page: its sub-locations and the items stored in it.</summary>
    /// <remarks>Read back by <see cref="PlaceViewModel.ApplyQueryAttributes"/>.</remarks>
    public static string Place(int locationId) => $"place?locationId={locationId}";

    /// <summary>
    /// The Find page pushed on top of the current one, searching in
    /// <paramref name="inLocationId"/> and everything inside it, or on the Places tab.
    /// </summary>
    /// <remarks>Read back by <see cref="FindViewModel.ApplyQueryAttributes"/>.</remarks>
    public static string Search(int? inLocationId = null, bool places = false)
    {
        // Focused, as the search toolbar was: the user tapped search to type.
        var route = "search?pushed=1&focus=1";
        if (inLocationId is int id)
            route += $"&in={id}";
        if (places)
            route += "&tab=places";
        return route;
    }

    /// <summary>The edit form for an existing location.</summary>
    /// <remarks>Read back by <see cref="AddLocationViewModel.ApplyQueryAttributes"/>.</remarks>
    public static string EditLocation(int locationId) => $"editlocation?locationId={locationId}";
}
