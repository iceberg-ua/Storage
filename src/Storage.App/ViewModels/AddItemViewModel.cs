using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Models;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

public partial class AddItemViewModel : ObservableObject
{
    private readonly IItemRepository _itemRepository;
    private readonly ILocationRepository _locationRepository;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private StorageLocation? _selectedLocation;

    [ObservableProperty]
    private ObservableCollection<StorageLocation> _locations = [];

    public AddItemViewModel(IItemRepository itemRepository, ILocationRepository locationRepository)
    {
        _itemRepository = itemRepository;
        _locationRepository = locationRepository;
    }

    [RelayCommand]
    private async Task LoadLocationsAsync()
    {
        var locations = await _locationRepository.GetAllAsync();
        Locations = new ObservableCollection<StorageLocation>(locations);
    }

    [RelayCommand]
    private async Task SaveItemAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await Shell.Current.DisplayAlertAsync("Error", "Please enter an item name", "OK");
            return;
        }

        var item = new Item
        {
            Name = Name,
            Description = Description,
            LocationId = SelectedLocation?.Id
        };

        await _itemRepository.AddAsync(item);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
