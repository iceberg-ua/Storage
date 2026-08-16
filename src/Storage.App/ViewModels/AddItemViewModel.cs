using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Models;
using Storage.Core.Repositories;
using Storage.Core.Services;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// Backs both "add" and "edit" for an item. Shell passes an optional "itemId" query
// parameter to edit an existing item, and an optional "locationId" to pre-select the
// location when adding (used by the shortcut on the location edit screen).
public partial class AddItemViewModel : ObservableObject, IQueryAttributable
{
    private const int MaxSuggestions = 5;

    private readonly IItemRepository _itemRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly ITagRepository _tagRepository;
    private readonly IPhotoService _photoService;

    // Every tag in the database, for autocomplete and for reusing the casing and
    // colour a tag is already stored under.
    private List<Tag> _allTags = [];

    private int _itemId;
    private int? _preselectedLocationId;
    private bool _isLoaded;

    // What the DB holds right now. Anything else in PhotoFileName is uncommitted,
    // which is what tells us which file to delete on save and which on the way out.
    private string? _savedPhotoFileName;
    private string? _capturedPhotoFileName;

    // Reconciliation runs when the page is left, not from any one exit handler, so
    // every route out is covered by construction. These two say when "left" is real:
    // the page also disappears when the camera page is pushed on top of it, and there
    // is nothing to discard once a save has committed.
    private bool _isAwaitingCamera;
    private bool _isCommitted;

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
    private ObservableCollection<Tag> _itemTags = [];

    [ObservableProperty]
    private string _tagInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTagSuggestions))]
    private ObservableCollection<Tag> _tagSuggestions = [];

    public bool HasTagSuggestions => TagSuggestions.Count > 0;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _pageTitle = "Add Item";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    [NotifyPropertyChangedFor(nameof(PhotoSource))]
    private string? _photoFileName;

    public bool HasPhoto => !string.IsNullOrEmpty(PhotoFileName);

    public ImageSource? PhotoSource =>
        _photoService.GetFullPath(PhotoFileName) is string path ? ImageSource.FromFile(path) : null;

    public AddItemViewModel(
        IItemRepository itemRepository,
        ILocationRepository locationRepository,
        ITagRepository tagRepository,
        IPhotoService photoService)
    {
        _itemRepository = itemRepository;
        _locationRepository = locationRepository;
        _tagRepository = tagRepository;
        _photoService = photoService;
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

        // Handed back by the camera page. Applied in LoadAsync so the swap can be
        // awaited — this method can't be.
        if (query.TryGetValue("photoFileName", out var photoFileName) &&
            Convert.ToString(photoFileName) is { Length: > 0 } capturedFileName)
        {
            _capturedPhotoFileName = capturedFileName;
        }

        IsEditMode = _itemId != 0;
        PageTitle = IsEditMode ? "Edit Item" : "Add Item";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        // We are back on this page, so the camera round-trip is over either way.
        _isAwaitingCamera = false;

        if (_capturedPhotoFileName is string captured)
        {
            _capturedPhotoFileName = null;
            await SetPendingPhotoAsync(captured);
        }

        // OnAppearing fires again when a pushed page is popped — don't discard edits in progress.
        if (_isLoaded)
            return;

        _isLoaded = true;

        var locations = await _locationRepository.GetAllAsync();
        Locations = new ObservableCollection<StorageLocation>(locations);

        var tags = await _tagRepository.GetAllAsync();
        _allTags = tags.ToList();

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
            ItemTags = new ObservableCollection<Tag>(item.Tags.OrderBy(t => t.Name));

            _savedPhotoFileName = item.PhotoPath;
            PhotoFileName = item.PhotoPath;
        }
        else if (_preselectedLocationId is int preselected)
        {
            SelectedLocation = Locations.FirstOrDefault(l => l.Id == preselected);
        }
    }

    // Typing filters the tags already in the database down to what is worth offering.
    partial void OnTagInputChanged(string value)
    {
        var input = value.Trim();

        TagSuggestions = input.Length == 0
            ? []
            : new ObservableCollection<Tag>(
                _allTags
                    .Where(t => t.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
                    .Where(t => !IsAlreadyOnItem(t.Name))
                    .Take(MaxSuggestions));
    }

    [RelayCommand]
    private void AddTag() => AddTagName(TagInput);

    [RelayCommand]
    private void SelectSuggestion(Tag tag) => AddTagName(tag.Name);

    [RelayCommand]
    private void RemoveTag(Tag tag) => ItemTags.Remove(tag);

    private void AddTagName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;

        if (!IsAlreadyOnItem(trimmed))
        {
            // Reuse the tag already stored under this name, so the chips can't show
            // "Tools" and "tools" as two different things, and so an existing tag
            // keeps its colour instead of appearing in a new one.
            var known = _allTags.FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));

            if (known is null)
            {
                // Detached, for display only — SetItemTagsAsync creates the real row
                // on save. The colour is picked the same way it will be there, so the
                // chip doesn't change colour under the user once saved.
                known = new Tag { Name = trimmed, Color = TagPalette.ForIndex(_allTags.Count) };

                // Known from here on, so retyping it in another case during this edit
                // lands on the same tag rather than making a second chip.
                _allTags.Add(known);
            }

            ItemTags.Add(known);
        }

        // Clearing the input also empties the suggestion list, via OnTagInputChanged.
        TagInput = string.Empty;
    }

    private bool IsAlreadyOnItem(string name) =>
        ItemTags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private async Task TakePhotoAsync()
    {
        // The page is about to disappear because it is being covered, not left.
        _isAwaitingCamera = true;
        await Shell.Current.GoToAsync("camera");
    }

    [RelayCommand]
    private async Task ChooseFromGalleryAsync()
    {
        try
        {
            var fileName = await _photoService.PickFromGalleryAsync();
            if (fileName is not null)
                await SetPendingPhotoAsync(fileName);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Error", $"That image could not be imported.\n\n{ex.Message}", "OK");
        }
    }

    [RelayCommand]
    private async Task RemovePhotoAsync()
    {
        await SetPendingPhotoAsync(null);
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

        // A tag half-typed in the entry counts as intended — saving shouldn't
        // silently drop it just because return was never pressed.
        AddTagName(TagInput);

        int savedItemId;

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
            item.PhotoPath = PhotoFileName;

            await _itemRepository.UpdateAsync(item);
            savedItemId = item.Id;
        }
        else
        {
            var added = await _itemRepository.AddAsync(new Item
            {
                Name = Name.Trim(),
                Description = description,
                Quantity = Quantity,
                LocationId = SelectedLocation?.Id,
                PhotoPath = PhotoFileName
            });

            savedItemId = added.Id;
        }

        // Tags are written separately: the new item needs its id first, and the
        // repository resolves each name to a shared row rather than storing text.
        await _tagRepository.SetItemTagsAsync(savedItemId, ItemTags.Select(t => t.Name));

        // Only now is the replaced photo safe to remove.
        if (_savedPhotoFileName is not null && _savedPhotoFileName != PhotoFileName)
            await _photoService.DeleteAsync(_savedPhotoFileName);

        _savedPhotoFileName = PhotoFileName;
        _isCommitted = true;

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        // Just navigate. The photo is reconciled on the way out, so this handler does
        // not have to remember to — and neither does any exit route added later.
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>
    /// Called when the page is leaving. Anything captured during this edit and never
    /// saved is an orphan from here on.
    /// </summary>
    public async Task ReconcilePhotoOnLeaveAsync()
    {
        if (_isAwaitingCamera || _isCommitted)
            return;

        if (PhotoFileName is not null && PhotoFileName != _savedPhotoFileName)
            await _photoService.DeleteAsync(PhotoFileName);

        PhotoFileName = _savedPhotoFileName;
    }

    // Swapping the pending photo drops the file it replaces, unless that file is
    // the one already stored against the item — that one only goes on a save.
    private async Task SetPendingPhotoAsync(string? fileName)
    {
        if (PhotoFileName is not null && PhotoFileName != _savedPhotoFileName)
            await _photoService.DeleteAsync(PhotoFileName);

        PhotoFileName = fileName;
    }
}
