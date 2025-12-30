using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

public partial class LocationsViewModel : ObservableObject
{
    private readonly ILocationRepository _locationRepository;

    [ObservableProperty]
    private ObservableCollection<StorageLocation> _locations = [];

    [ObservableProperty]
    private bool _isLoading;

    public LocationsViewModel(ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
    }

    [RelayCommand]
    private async Task LoadLocationsAsync()
    {
        IsLoading = true;
        try
        {
            var locations = await _locationRepository.GetAllAsync();
            Locations = new ObservableCollection<StorageLocation>(locations);
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
            Locations.Remove(location);
        }
    }
}
