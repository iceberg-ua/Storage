using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
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
    private readonly PlacePicker _placePicker;
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

    /// <summary>True while the camera page, the gallery picker or the place picker covers the form.</summary>
    public bool IsCovered { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    private string? _photoFileName;

    public bool HasPhoto => PhotoFileName is not null;

    [ObservableProperty]
    private string _name = string.Empty;

    public FieldError NameError { get; } = new();

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParentPath))]
    private StorageLocation? _parentLocation;

    // Every location this one may sit inside: all of them but itself and what is inside
    // it. Nothing binds it; the form shows only the chosen one, as a path.
    private List<StorageLocation> _availableParents = [];
    private IReadOnlySet<int> _excludedParentIds = new HashSet<int>();

    /// <summary>"Garage › Shelf 2", or "Top level".</summary>
    public string ParentPath
    {
        get
        {
            if (ParentLocation is null)
                return "Top level";

            return LocationPath.Of(ParentLocation.Id, _availableParents.ToDictionary(l => l.Id));
        }
    }

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _pageTitle = "Add Location";

    public AddLocationViewModel(
        ILocationRepository locationRepository,
        IPhotoService photoService,
        PlacePicker placePicker)
    {
        _locationRepository = locationRepository;
        _photoService = photoService;
        _placePicker = placePicker;
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
        IsCovered = false;
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

        await LoadAvailableParentsAsync();

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
            ParentLocation = _availableParents.FirstOrDefault(l => l.Id == location.ParentId);

            _savedPhotoFileName = location.PhotoFileName;

            // The gallery button works before this load finishes. A photo picked in that
            // window is the user's choice, so the saved one must not overwrite it.
            if (_stagedPhotoFileNames.Count == 0)
                PhotoFileName = _savedPhotoFileName;
        }
        else if (_parentId is int parent)
        {
            ParentLocation = _availableParents.FirstOrDefault(l => l.Id == parent);
        }
    }

    private async Task LoadAvailableParentsAsync()
    {
        var locations = await _locationRepository.GetAllAsync();

        // A location can't be its own parent, nor sit inside something stored in it.
        var excluded = IsEditMode
            ? new HashSet<int>(await _locationRepository.GetDescendantIdsAsync(_locationId)) { _locationId }
            : new HashSet<int>();

        _excludedParentIds = excluded;
        _availableParents = locations.Where(l => !excluded.Contains(l.Id)).ToList();
    }

    // The place picker, as on the item form: a flat list cannot tell two "Shelf 2"s
    // apart. Itself and everything inside it are left out of the picker, so a move that
    // would make a loop cannot be chosen.
    [RelayCommand]
    private async Task PickParentAsync()
    {
        // The picker covers the page, which then disappears; without this the form would
        // take that as being left and throw away the photo taken on it.
        IsCovered = true;
        try
        {
            // Read afresh each time, so the picker leaves out exactly what is inside this
            // location now (a location created inside it since the form opened included),
            // and a tap before the first load finished still has the right exclusions.
            await LoadAvailableParentsAsync();

            var pick = await _placePicker.PickAsync(new PlacePickRequest(
                "Put it inside…",
                string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
                ParentLocation?.Id,
                "Put it in {0}",
                AllowNone: true,
                NoneName: "Top level",
                NoneMeta: "Not inside another location",
                NoneInSentence: "the top level",
                ExcludedIds: _excludedParentIds));

            if (pick is null)
                return;

            if (pick.LocationId is not int id)
            {
                ParentLocation = null;
                return;
            }

            // The picker reads the locations afresh, so it can offer one added since the
            // list above was read; reload once for it.
            if (_availableParents.All(l => l.Id != id))
                await LoadAvailableParentsAsync();

            // Still not a parent this location may have (it was moved inside this one
            // meanwhile, or deleted): keep the old choice and say so, rather than quietly
            // putting the location at the top level.
            var picked = _availableParents.FirstOrDefault(l => l.Id == id);
            if (picked is null)
            {
                await Shell.Current.DisplayAlertAsync(
                    null, "That location can't hold this one any more. The place it was in is kept.", "OK");
                return;
            }

            ParentLocation = picked;
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync(
                "Couldn't set where it goes", $"Try again in a moment.\n\n{ex.Message}".TrimEnd(), "OK");
        }
        finally
        {
            IsCovered = false;
        }
    }

    // The message has done its job once the user starts typing a name.
    partial void OnNameChanged(string value) => NameError.Clear();

    // One slot, so there is no limit to enforce: a new photo replaces the current one.
    [RelayCommand]
    private async Task TakePhotoAsync()
    {
        // The page is about to disappear because it is being covered, not left.
        IsCovered = true;
        await Shell.Current.GoToAsync("camera");
    }

    [RelayCommand]
    private async Task ChooseFromGalleryAsync()
    {
        IsCovered = true;
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
            IsCovered = false;
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

        // The parent was chosen when the form opened or when the picker closed; it may
        // have been deleted since, from another tab. Saving would then fail the foreign
        // key, so say so and let the user choose again rather than lose the form.
        if (ParentLocation is { } chosenParent && await _locationRepository.GetByIdAsync(chosenParent.Id) is null)
        {
            ParentLocation = null;
            await Shell.Current.DisplayAlertAsync(
                "Choose where it goes again",
                $"{chosenParent.Name} no longer exists, so it can't hold this location. It is set to Top level for now; change it or save as it is.",
                "OK");
            return;
        }

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
            catch (Exception ex)
            {
                await ShowSaveFailedAsync(ex);
                return;
            }
        }
        else
        {
            try
            {
                await _locationRepository.AddAsync(new StorageLocation
                {
                    Name = Name.Trim(),
                    Description = description,
                    PhotoFileName = PhotoFileName,
                    ParentId = ParentLocation?.Id
                });
            }
            catch (Exception ex)
            {
                await ShowSaveFailedAsync(ex);
                return;
            }
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

    // Still possible after the check above: the parent can go in the moment between the
    // check and the write. Nothing was saved, and the form stays as it is, photo
    // included, so the user can try again. Saves run from a command, where an escaping
    // exception would end the app.
    private static Task ShowSaveFailedAsync(Exception ex) =>
        Shell.Current.DisplayAlertAsync(
            "Couldn't save the location",
            $"Nothing was saved. Check where it goes and try again.\n\n{ex.Message}".TrimEnd(),
            "OK");

    // Shortcut: open the create screen on an item, with this location already selected.
    [RelayCommand]
    private async Task AddItemHereAsync()
    {
        if (!IsEditMode)
            return;

        // The edit stays open underneath, unsaved photo included.
        _isCoveredByCreate = true;
        await Shell.Current.GoToAsync(Routes.Create(CreateKind.Item, _locationId));
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
        if (IsCovered || _isCoveredByCreate || _isCommitted)
            return;

        await _photoService.DeleteAllAsync(
            _stagedPhotoFileNames
                .Where(f => !string.Equals(f, _savedPhotoFileName, StringComparison.OrdinalIgnoreCase))
                .ToList());

        _stagedPhotoFileNames.Clear();
        PhotoFileName = _savedPhotoFileName;
    }
}
