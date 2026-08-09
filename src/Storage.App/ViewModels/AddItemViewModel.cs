using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Models;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// Backs both "add" and "edit" for an item. Shell passes an optional "itemId" query
// parameter to edit an existing item, and an optional "locationId" to pre-select the
// location when adding (used by the shortcut on the location edit screen).
public partial class AddItemViewModel : ObservableObject, IQueryAttributable
{
    private readonly IItemRepository _itemRepository;
    private readonly ILocationRepository _locationRepository;

    private int _itemId;
    private int? _preselectedLocationId;
    private bool _isLoaded;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private int _quantity = 1;

    [ObservableProperty]
    private StorageLocation? _selectedLocation;

    [ObservableProperty]
    private ObservableCollection<StorageLocation> _locations = [];

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _pageTitle = "Add Item";

    public AddItemViewModel(IItemRepository itemRepository, ILocationRepository locationRepository)
    {
        _itemRepository = itemRepository;
        _locationRepository = locationRepository;
    }

    // Shell calls this on the page's BindingContext before the page appears.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("itemId", out var itemId) &&
            int.TryParse(Convert.ToString(itemId), out var parsedItemId))
        {
            _itemId = parsedItemId;
        }

        if (query.TryGetValue("locationId", out var locationId) &&
            int.TryParse(Convert.ToString(locationId), out var parsedLocationId))
        {
            _preselectedLocationId = parsedLocationId;
        }

        IsEditMode = _itemId != 0;
        PageTitle = IsEditMode ? "Edit Item" : "Add Item";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        // OnAppearing fires again when a pushed page is popped — don't discard edits in progress.
        if (_isLoaded)
            return;

        _isLoaded = true;

        var locations = await _locationRepository.GetAllAsync();
        Locations = new ObservableCollection<StorageLocation>(locations);

        if (IsEditMode)
        {
            var item = await _itemRepository.GetByIdAsync(_itemId);
            if (item is null)
            {
                await Shell.Current.DisplayAlertAsync("Error", "This item no longer exists", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            Name = item.Name;
            Description = item.Description ?? string.Empty;
            Quantity = item.Quantity;
            SelectedLocation = Locations.FirstOrDefault(l => l.Id == item.LocationId);
        }
        else if (_preselectedLocationId is int preselected)
        {
            SelectedLocation = Locations.FirstOrDefault(l => l.Id == preselected);
        }
    }

    [RelayCommand]
    private async Task SaveItemAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await Shell.Current.DisplayAlertAsync("Error", "Please enter an item name", "OK");
            return;
        }

        if (Quantity < 1)
            Quantity = 1;

        var description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

        if (IsEditMode)
        {
            var item = await _itemRepository.GetByIdAsync(_itemId);
            if (item is null)
            {
                await Shell.Current.DisplayAlertAsync("Error", "This item no longer exists", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            item.Name = Name.Trim();
            item.Description = description;
            item.Quantity = Quantity;
            item.LocationId = SelectedLocation?.Id;

            await _itemRepository.UpdateAsync(item);
        }
        else
        {
            await _itemRepository.AddAsync(new Item
            {
                Name = Name.Trim(),
                Description = description,
                Quantity = Quantity,
                LocationId = SelectedLocation?.Id
            });
        }

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
