using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Repositories;
using Storage.Core.Services;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// The tab the app opens on: the top-level locations. Opening one pushes that location's
// own page, which shows what is nested in it. Its search is the same one the items page
// has — same toolbar, same toggles — so looking for something from the first screen
// does not mean knowing which tab to be on first.
public partial class LocationsViewModel : ObservableObject
{
    private readonly ILocationRepository _locationRepository;
    private readonly IPhotoService _photoService;

    public SearchViewModel Search { get; }

    [ObservableProperty]
    private bool _isLoading;

    public LocationsViewModel(
        ILocationRepository locationRepository,
        IItemRepository itemRepository,
        IPhotoService photoService)
    {
        _locationRepository = locationRepository;
        _photoService = photoService;

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
        var confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Location",
            $"Are you sure you want to delete '{location.Name}'?",
            "Delete",
            "Cancel");

        if (confirm)
        {
            // The file goes only after the row: a delete that fails must not leave the
            // location pointing at a photo that is no longer there.
            StorageLocation? deleted;
            try
            {
                deleted = await _locationRepository.DeleteAsync(location.Id);
            }
            catch (Exception ex)
            {
                // Most likely it still has locations inside it, which the schema refuses.
                await Shell.Current.DisplayAlertAsync(
                    "Couldn't delete location", $"Move or delete what is inside it first.\n\n{ex.Message}".TrimEnd(), "OK");
                return;
            }

            await _photoService.DeleteAsync(deleted?.PhotoFileName);

            Search.Locations.Remove(location);
        }
    }
}
