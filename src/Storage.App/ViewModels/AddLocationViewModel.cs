using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Repositories;
using Storage.Core.Services;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// Backs both "add" and "edit" for a location. The edit page binds it directly and is
// pushed with the "locationId" to edit; the create page reaches it through
// CreateViewModel, which leaves it in add mode and preselects a parent with PreselectParent.
//
// A location has at most one photo. Its files follow the same rules as an item's: a photo
// taken or picked here is written straight away but only becomes the location's on save,
// and whatever the edit started with or wrote and no longer uses is deleted then — or,
// for an edit that is abandoned, when the page is left.
public partial class AddLocationViewModel : ObservableObject, IQueryAttributable
{
    private readonly ILocationRepository _locationRepository;
    private readonly IPhotoService _photoService;

    private int _locationId;
    private int? _parentId;
    private bool _isLoaded;

    // What the DB holds right now.
    private string? _savedPhotoFileName;

    // Every file this edit has written, including ones since replaced or removed.
    private readonly HashSet<string> _stagedPhotoFileNames = new(StringComparer.OrdinalIgnoreCase);

    private string? _capturedPhotoFileName;

    // See AddItemViewModel: the page also disappears while the camera or the gallery
    // covers it, and there is nothing to discard once a save has committed.
    private bool _isCommitted;

    // Set while "Add Item Here" has the create page on top: the form is covered, not left.
    private bool _isCoveredByCreate;

    /// <summary>True while the camera page or the gallery picker covers the form.</summary>
    public bool IsAwaitingPhotoSource { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    [NotifyPropertyChangedFor(nameof(PhotoSource))]
    private string? _photoFileName;

    public bool HasPhoto => PhotoFileName is not null;

    public ImageSource? PhotoSource =>
        _photoService.GetFullPath(PhotoFileName) is string path ? ImageSource.FromFile(path) : null;

    [ObservableProperty]
    private string _name = string.Empty;

    public FieldError NameError { get; } = new();

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private StorageLocation? _parentLocation;

    [ObservableProperty]
    private ObservableCollection<StorageLocation> _availableParents = [];

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _pageTitle = "Add Location";

    public AddLocationViewModel(ILocationRepository locationRepository, IPhotoService photoService)
    {
        _locationRepository = locationRepository;
        _photoService = photoService;
    }

    // The edit page's entry point: Shell calls this on its BindingContext with the
    // "locationId" to edit, and again when the camera pops back with a "photoFileName".
    // The create page does not go through here.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("locationId", out var locationId) &&
            int.TryParse(Convert.ToString(locationId), out var parsedLocationId))
        {
            _locationId = parsedLocationId;
        }

        if (query.TryGetValue("photoFileName", out var photoFileName) &&
            Convert.ToString(photoFileName) is { Length: > 0 } capturedFileName)
        {
            AcceptCapturedPhoto(capturedFileName);
        }

        IsEditMode = _locationId != 0;
        PageTitle = IsEditMode ? "Edit Location" : "Add Location";
    }

    /// <summary>The parent a new location starts under. Applied on the first load.</summary>
    public void PreselectParent(int? parentId) => _parentId = parentId;

    /// <summary>A photo the camera page handed back. Shown on the next load.</summary>
    public void AcceptCapturedPhoto(string fileName) => _capturedPhotoFileName = fileName;

    [RelayCommand]
    private async Task LoadAsync()
    {
        // We are back on this page, so the camera round-trip is over either way.
        IsAwaitingPhotoSource = false;
        _isCoveredByCreate = false;

        if (_capturedPhotoFileName is string captured)
        {
            _capturedPhotoFileName = null;
            SetPhoto(captured);
        }

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
                await Shell.Current.DisplayAlertAsync(null, "This location no longer exists", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            Name = location.Name;
            Description = location.Description ?? string.Empty;
            ParentLocation = AvailableParents.FirstOrDefault(l => l.Id == location.ParentId);

            _savedPhotoFileName = location.PhotoFileName;
            PhotoFileName = _savedPhotoFileName;
        }
        else if (_parentId is int parent)
        {
            ParentLocation = AvailableParents.FirstOrDefault(l => l.Id == parent);
        }
    }

    // The message has done its job once the user starts typing a name.
    partial void OnNameChanged(string value) => NameError.Clear();

    // One slot, so there is no limit to enforce: a new photo replaces the current one.
    [RelayCommand]
    private async Task TakePhotoAsync()
    {
        // The page is about to disappear because it is being covered, not left.
        IsAwaitingPhotoSource = true;
        await Shell.Current.GoToAsync("camera");
    }

    [RelayCommand]
    private async Task ChooseFromGalleryAsync()
    {
        IsAwaitingPhotoSource = true;
        try
        {
            var fileName = await _photoService.PickFromGalleryAsync();
            if (fileName is not null)
                SetPhoto(fileName);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync(
                "Couldn't import photo", $"Try a different image.\n\n{ex.Message}".TrimEnd(), "OK");
        }
        finally
        {
            IsAwaitingPhotoSource = false;
        }
    }

    // The file stays on disk until the edit is resolved, so a cancel can put it back.
    [RelayCommand]
    private void RemovePhoto() => PhotoFileName = null;

    private void SetPhoto(string fileName)
    {
        _stagedPhotoFileNames.Add(fileName);
        PhotoFileName = fileName;
    }

    [RelayCommand]
    private async Task SaveLocationAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError.Show("Please enter a location name");
            return;
        }

        var description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

        if (IsEditMode)
        {
            var location = await _locationRepository.GetByIdAsync(_locationId);
            if (location is null)
            {
                await Shell.Current.DisplayAlertAsync(null, "This location no longer exists", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            location.Name = Name.Trim();
            location.Description = description;
            location.PhotoFileName = PhotoFileName;
            location.ParentId = ParentLocation?.Id;

            try
            {
                await _locationRepository.UpdateAsync(location);
            }
            catch (LocationCycleException) when (ParentLocation is { } parent)
            {
                // The picker hides descendants, but the tree may have changed since it loaded.
                await Shell.Current.DisplayAlertAsync(
                    "Can't use that parent", $"{parent.Name} is inside {Name.Trim()}, so it can't be its parent", "OK");
                return;
            }
        }
        else
        {
            await _locationRepository.AddAsync(new StorageLocation
            {
                Name = Name.Trim(),
                Description = description,
                PhotoFileName = PhotoFileName,
                ParentId = ParentLocation?.Id
            });
        }

        // Only now are the dropped files safe to remove: everything this edit started
        // with or wrote, other than the photo the location now has.
        await _photoService.DeleteAllAsync(
            _stagedPhotoFileNames
                .Append(_savedPhotoFileName)
                .OfType<string>()
                .Where(f => !string.Equals(f, PhotoFileName, StringComparison.OrdinalIgnoreCase))
                .ToList());

        _savedPhotoFileName = PhotoFileName;
        _stagedPhotoFileNames.Clear();
        _isCommitted = true;

        await Shell.Current.GoToAsync("..");
    }

    // Shortcut: open the create screen on an item, with this location already selected.
    [RelayCommand]
    private async Task AddItemHereAsync()
    {
        if (!IsEditMode)
            return;

        // The edit stays open underneath, unsaved photo included.
        _isCoveredByCreate = true;
        await Shell.Current.GoToAsync($"create?kind=item&locationId={_locationId}");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        // Just navigate. The photo is reconciled on the way out, whichever way that is.
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>
    /// Called when the page is leaving. A photo written during this edit and never saved
    /// is an orphan from here on.
    /// </summary>
    public async Task ReconcilePhotoOnLeaveAsync()
    {
        if (IsAwaitingPhotoSource || _isCoveredByCreate || _isCommitted)
            return;

        await _photoService.DeleteAllAsync(
            _stagedPhotoFileNames
                .Where(f => !string.Equals(f, _savedPhotoFileName, StringComparison.OrdinalIgnoreCase))
                .ToList());

        _stagedPhotoFileNames.Clear();
        PhotoFileName = _savedPhotoFileName;
    }
}
