using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// The tab the app opens on: the top-level locations, or what is nested in the current
// location once one is opened. Its search is the same one the items page has — same
// toolbar, same toggles — so looking for something from the first screen does not
// mean knowing which tab to be on first.
public partial class LocationsViewModel : ObservableObject
{
    private readonly ILocationRepository _locationRepository;

    public LocationContext Context { get; }

    public SearchViewModel Search { get; }

    [ObservableProperty]
    private bool _isLoading;

    public LocationsViewModel(
        ILocationRepository locationRepository,
        IItemRepository itemRepository,
        LocationContext locationContext)
    {
        _locationRepository = locationRepository;
        Context = locationContext;

        // Locations first, since that is what this page is for; the toggle still
        // reaches items without leaving the page.
        Search = new SearchViewModel(itemRepository, locationRepository, locationContext, locationsByDefault: true);
    }

    // Same as the Items tab: follow the context only while on screen, so the singleton
    // does not keep this view model alive.
    public void Attach()
    {
        Context.Changed -= OnContextChanged;
        Context.Changed += OnContextChanged;
    }

    public void Detach() => Context.Changed -= OnContextChanged;

    private async void OnContextChanged(object? sender, EventArgs e) => await LoadAsync();

    [RelayCommand]
    private async Task LoadLocationsAsync()
    {
        // Same as the Items tab: refresh the path first, and reload here only if that
        // did not already move the user and reload through Changed.
        var before = Context.CurrentId;
        await Context.RefreshAsync();

        if (Context.CurrentId == before)
            await LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Search.ScopeTo(Context.CurrentId);
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
        // Inside a location, a new one is nested in it by default.
        var route = Context.CurrentId is int id ? $"addlocation?parentId={id}" : "addlocation";
        await Shell.Current.GoToAsync(route);
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
