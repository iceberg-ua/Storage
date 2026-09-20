using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

// Backs the "Items" tab and, when Shell passes a "locationId", the same page scoped
// to a single location (pushed from the Locations list). Everything to do with the
// list itself — searching it, browsing it, what it shows when empty — lives in
// Search, which the Locations page composes the same way.
public partial class ItemsViewModel : ObservableObject, IQueryAttributable
{
    private readonly ILocationRepository _locationRepository;

    private int? _locationId;

    public SearchViewModel Search { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLocationView;

    [ObservableProperty]
    private string _pageTitle = "My Items";

    public ItemsViewModel(
        IItemRepository itemRepository,
        ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
        Search = new SearchViewModel(itemRepository, locationRepository);
    }

    // Shell calls this on the page's BindingContext before the page appears.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("locationId", out var locationId) &&
            int.TryParse(Convert.ToString(locationId), out var parsedLocationId))
        {
            _locationId = parsedLocationId;
        }

        IsLocationView = _locationId is not null;
        Search.ScopeTo(_locationId);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            if (_locationId is int id)
            {
                // Re-read the location each time so a rename made on the edit screen
                // is reflected when we come back to this page.
                var location = await _locationRepository.GetByIdAsync(id);
                PageTitle = location?.Name ?? "Location";
            }

            await Search.LoadAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToAddItemAsync()
    {
        // In a location view, new items belong to that location by default.
        var route = _locationId is int id ? $"additem?locationId={id}" : "additem";
        await Shell.Current.GoToAsync(route);
    }

    [RelayCommand]
    private async Task EditLocationAsync()
    {
        if (_locationId is not int id)
            return;

        await Shell.Current.GoToAsync($"addlocation?locationId={id}");
    }
}
