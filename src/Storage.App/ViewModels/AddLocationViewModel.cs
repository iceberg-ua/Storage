using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// Backs both "add" and "edit" for a location. Shell passes an optional "locationId"
// query parameter to edit an existing location, or a "parentId" to preselect where a
// new one goes.
public partial class AddLocationViewModel : ObservableObject, IQueryAttributable
{
    private readonly ILocationRepository _locationRepository;

    private int _locationId;
    private int? _parentId;
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

        if (query.TryGetValue("parentId", out var parentId) &&
            int.TryParse(Convert.ToString(parentId), out var parsedParentId))
        {
            _parentId = parsedParentId;
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

        // A location can't be its own parent, nor sit inside something stored in it.
        var descendantIds = IsEditMode
            ? await _locationRepository.GetDescendantIdsAsync(_locationId)
            : new HashSet<int>();

        AvailableParents = new ObservableCollection<StorageLocation>(
            locations.Where(l => l.Id != _locationId && !descendantIds.Contains(l.Id)));

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
        else if (_parentId is int parent)
        {
            ParentLocation = AvailableParents.FirstOrDefault(l => l.Id == parent);
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

            // The picker already hides these, but the tree may have changed since it loaded.
            if (ParentLocation is { } parent &&
                (await _locationRepository.GetDescendantIdsAsync(_locationId)).Contains(parent.Id))
            {
                await Shell.Current.DisplayAlertAsync(
                    "Error", $"{parent.Name} is inside {location.Name}, so it can't be its parent", "OK");
                return;
            }

            location.Name = Name.Trim();
            location.ParentId = ParentLocation?.Id;

            try
            {
                await _locationRepository.UpdateAsync(location);
            }
            catch (InvalidOperationException)
            {
                // The tree changed between the check above and the save.
                await Shell.Current.DisplayAlertAsync(
                    "Error", $"{ParentLocation!.Name} is inside {Name.Trim()}, so it can't be its parent", "OK");
                return;
            }
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
