using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

public partial class AddLocationViewModel : ObservableObject
{
    private readonly ILocationRepository _locationRepository;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private StorageLocation? _parentLocation;

    [ObservableProperty]
    private ObservableCollection<StorageLocation> _availableParents = [];

    public AddLocationViewModel(ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
    }

    [RelayCommand]
    private async Task LoadParentLocationsAsync()
    {
        var locations = await _locationRepository.GetAllAsync();
        AvailableParents = new ObservableCollection<StorageLocation>(locations);
    }

    [RelayCommand]
    private async Task SaveLocationAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await Shell.Current.DisplayAlertAsync("Error", "Please enter a location name", "OK");
            return;
        }

        var location = new StorageLocation
        {
            Name = Name,
            ParentId = ParentLocation?.Id
        };

        await _locationRepository.AddAsync(location);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
