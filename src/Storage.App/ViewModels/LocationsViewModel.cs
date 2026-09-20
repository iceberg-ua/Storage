using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// The tab the app opens on. It lists locations, but its search is the same one the
// items page has — same toolbar, same toggles — so looking for something from the
// first screen does not mean knowing which tab to be on first.
public partial class LocationsViewModel : ObservableObject
{
    private readonly ILocationRepository _locationRepository;

    public SearchViewModel Search { get; }

    [ObservableProperty]
    private bool _isLoading;

    public LocationsViewModel(
        ILocationRepository locationRepository,
        IItemRepository itemRepository)
    {
        _locationRepository = locationRepository;

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
    private async Task NavigateToAddLocationAsync()
    {
        await Shell.Current.GoToAsync("addlocation");
    }

    [RelayCommand]
    private async Task DeleteLocationAsync(StorageLocation location)
    {
        var confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Location",
            $"Are you sure you want to delete '{location.Name}'?",
            "Delete",
            "Cancel");

        if (confirm)
        {
            await _locationRepository.DeleteAsync(location.Id);
            Search.Locations.Remove(location);
        }
    }
}
