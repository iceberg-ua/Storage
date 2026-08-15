using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Models;
using Storage.Core.Repositories;
using Storage.Core.Services;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// Backs the "Items" tab and, when Shell passes a "locationId", the same page scoped
// to a single location (pushed from the Locations list). In the scoped view the page
// gains a second tab listing that location's child locations.
public partial class ItemsViewModel : ObservableObject, IQueryAttributable
{
    private readonly IItemRepository _itemRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IPhotoService _photoService;

    private int? _locationId;

    [ObservableProperty]
    private ObservableCollection<Item> _items = [];

    [ObservableProperty]
    private ObservableCollection<StorageLocation> _childLocations = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLocationView;

    // The strip only earns its space once there is a second tab worth switching to.
    [ObservableProperty]
    private bool _showTabStrip;

    [ObservableProperty]
    private string _pageTitle = "My Items";

    [ObservableProperty]
    private string _emptyMessage = "Tap ➕ to get started";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChildLocationsTabSelected))]
    private bool _isItemsTabSelected = true;

    public bool IsChildLocationsTabSelected => !IsItemsTabSelected;

    public ItemsViewModel(
        IItemRepository itemRepository,
        ILocationRepository locationRepository,
        IPhotoService photoService)
    {
        _itemRepository = itemRepository;
        _locationRepository = locationRepository;
        _photoService = photoService;
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

        if (IsLocationView)
            EmptyMessage = "Tap ➕ to store something here";
    }

    [RelayCommand]
    private async Task LoadItemsAsync()
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

                var items = await _itemRepository.GetByLocationIdAsync(id);
                Items = new ObservableCollection<Item>(items);

                var children = await _locationRepository.GetChildrenAsync(id);
                ChildLocations = new ObservableCollection<StorageLocation>(children);

                ShowTabStrip = ChildLocations.Count > 0;

                // Without the strip there is no control to switch back, so the
                // items list has to be the one on show.
                if (!ShowTabStrip)
                    IsItemsTabSelected = true;
            }
            else
            {
                var items = await _itemRepository.GetAllAsync();
                Items = new ObservableCollection<Item>(items);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectItemsTab() => IsItemsTabSelected = true;

    [RelayCommand]
    private void SelectChildLocationsTab() => IsItemsTabSelected = false;

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

    // Drill further down the hierarchy into a child location.
    [RelayCommand]
    private async Task OpenChildLocationAsync(StorageLocation location)
    {
        await Shell.Current.GoToAsync($"locationitems?locationId={location.Id}");
    }

    [RelayCommand]
    private async Task EditItemAsync(Item item)
    {
        await Shell.Current.GoToAsync($"additem?itemId={item.Id}");
    }

    [RelayCommand]
    private async Task DeleteItemAsync(Item item)
    {
        var confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Item",
            $"Are you sure you want to delete '{item.Name}'?",
            "Delete",
            "Cancel");

        if (confirm)
        {
            await _itemRepository.DeleteAsync(item.Id);
            await _photoService.DeleteAsync(item.PhotoPath);
            Items.Remove(item);
        }
    }
}
