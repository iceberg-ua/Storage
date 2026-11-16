using Storage.App.Views;

namespace Storage.App.Services;

/// <summary>
/// Jumps to a location's page from the path at the top of another one.
/// </summary>
public static class PlaceNavigation
{
    /// <summary>
    /// Shows <paramref name="locationId"/>'s page, or the Locations tab when it is null.
    /// If that page is already further down the stack — the usual case, since the path
    /// lists the locations opened on the way here — this pops back to it instead of
    /// pushing a second copy, so Back keeps meaning "up one level".
    /// </summary>
    public static async Task ShowAsync(int? locationId)
    {
        if (locationId is not int id)
        {
            // Pop this tab first: a place opened from Home would otherwise stay pushed on
            // the Home tab, and the next tap on Home would land on it instead of Home.
            await Shell.Current.Navigation.PopToRootAsync(animated: false);
            await Shell.Current.GoToAsync("//locations");
            return;
        }

        var stack = Shell.Current.Navigation.NavigationStack;
        for (var i = stack.Count - 2; i >= 0; i--)
        {
            if (stack[i] is PlacePage page && page.LocationId == id)
            {
                var levels = stack.Count - 1 - i;
                await Shell.Current.GoToAsync(string.Join('/', Enumerable.Repeat("..", levels)));
                return;
            }
        }

        // Reached some other way, such as a search result: the ancestor was never opened.
        await Shell.Current.GoToAsync(Routes.Place(id));
    }
}
