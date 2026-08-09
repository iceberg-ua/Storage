using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// Backs both "add" and "edit" for a location. Shell passes an optional "locationId"
// query parameter to edit an existing location.
public partial class AddLocationViewModel : ObservableObject, IQueryAttributable
{
    private readonly ILocationRepository _locationRepository;

    private int _locationId;
    private bool _isLoaded;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private StorageLocation? _parentLocation;

    [ObservableProperty]
    private ObservableCollection<StorageLocation> _availableParents = [];

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _pageTitle = "Add Location";

    public AddLocationViewModel(ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
    }

    // Shell calls this on the page's BindingContext before the page appears.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("locationId", out var locationId) &&
            int.TryParse(Convert.ToString(locationId), out var parsedLocationId))
        {
            _locationId = parsedLocationId;
        }

        IsEditMode = _locationId != 0;
        PageTitle = IsEditMode ? "Edit Location" : "Add Location";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        // OnAppearing fires again when a pushed page is popped — don't discard edits in progress.
        if (_isLoaded)
            return;

        _isLoaded = true;

        var locations = await _locationRepository.GetAllAsync();

        // A location can't be its own parent.
        AvailableParents = new ObservableCollection<StorageLocation>(
            locations.Where(l => l.Id != _locationId));

        if (IsEditMode)
        {
            var location = await _locationRepository.GetByIdAsync(_locationId);
            if (location is null)
            {
                await Shell.Current.DisplayAlertAsync("Error", "This location no longer exists", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            Name = location.Name;
            ParentLocation = AvailableParents.FirstOrDefault(l => l.Id == location.ParentId);
        }
    }

    [RelayCommand]
    private async Task SaveLocationAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await Shell.Current.DisplayAlertAsync("Error", "Please enter a location name", "OK");
            return;
        }

        if (IsEditMode)
        {
            var location = await _locationRepository.GetByIdAsync(_locationId);
            if (location is null)
            {
                await Shell.Current.DisplayAlertAsync("Error", "This location no longer exists", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            location.Name = Name.Trim();
            location.ParentId = ParentLocation?.Id;

            await _locationRepository.UpdateAsync(location);
        }
        else
        {
            await _locationRepository.AddAsync(new StorageLocation
            {
                Name = Name.Trim(),
                ParentId = ParentLocation?.Id
            });
        }

        await Shell.Current.GoToAsync("..");
    }

    // Shortcut: open the add-item form with this location already selected.
    [RelayCommand]
    private async Task AddItemHereAsync()
    {
        if (!IsEditMode)
            return;

        await Shell.Current.GoToAsync($"additem?locationId={_locationId}");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
