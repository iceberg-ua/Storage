using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// The tab the app opens on: the top-level locations. Opening one pushes that location's
// own page, which shows what is nested in it. Its search is the same one the items page
// has — same toolbar, same toggles — so looking for something from the first screen
// does not mean knowing which tab to be on first.
public partial class LocationsViewModel : ObservableObject
{
    private readonly LocationDeletion _deletion;

    public SearchViewModel Search { get; }

    [ObservableProperty]
    private bool _isLoading;

    public LocationsViewModel(
        ILocationRepository locationRepository,
        IItemRepository itemRepository,
        LocationDeletion deletion)
    {
        _deletion = deletion;

        // Locations first, since that is what this page is for; the toggle still
        // reaches items without leaving the page.
        Search = new SearchViewModel(itemRepository, locationRepository, locationsByDefault: true);
    }

    [RelayCommand]
    private async Task LoadLocationsAsync()
    {
        IsLoading = true;
        try
        {
            await Search.LoadAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task NavigateToCreateAsync() =>
        Shell.Current.GoToAsync(Routes.Create(CreateKind.Location));

    // A new item at the top level; inside a location, its own page adds one there.
    [RelayCommand]
    private Task NewItemAsync() => Shell.Current.GoToAsync(Routes.Create(CreateKind.Item));

    [RelayCommand]
    private async Task DeleteLocationAsync(StorageLocation location)
    {
        if (await _deletion.ConfirmAndDeleteAsync(location.Id, location.Name))
            Search.Locations.Remove(location);
    }
}
