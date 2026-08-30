using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

    // A product rule, not a schema one: the database is happy to join an item to any
    // number of tags, but a row in the list only has space for a handful.
    public const int MaxTagsPerItem = 5;

    // Also a product rule. Ten is enough to show an item from every side and catch its
    // label, and it keeps one item's share of device storage bounded.
    public const int MaxPhotosPerItem = 10;

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

    // What the DB holds right now, in order. Anything in Photos that is not in here is
    // uncommitted, which is what tells us which files to delete on save.
    private List<string> _savedPhotoFileNames = [];

    // Every file this edit has written, whether it is still in Photos or not: a photo
    // that was taken and then removed has already cost a file on disk.
    private readonly HashSet<string> _stagedPhotoFileNames = new(StringComparer.OrdinalIgnoreCase);

    private string? _capturedPhotoFileName;

    // Reconciliation runs when the page is left, not from any one exit handler, so
    // every route out is covered by construction. These two say when "left" is real:
    // the page also disappears when a photo source is put on top of it, and there is
    // nothing to discard once a save has committed.
    private bool _isAwaitingPhotoSource;
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

    public bool CanAddMoreTags => ItemTags.Count < MaxTagsPerItem;

    public bool IsAtTagLimit => !CanAddMoreTags;

    public string TagLimitMessage => $"Maximum {MaxTagsPerItem} tags per item";

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

    // The photo strip, in display order. The first is the primary — the one the item
    // list shows — so ordering the set and choosing the primary are one gesture.
    [ObservableProperty]
    private ObservableCollection<PhotoSlot> _photos = [];

    public bool HasPhotos => Photos.Count > 0;

    public bool CanAddMorePhotos => Photos.Count < MaxPhotosPerItem;

    public bool IsAtPhotoLimit => !CanAddMorePhotos;

    public string PhotoLimitMessage => $"Maximum {MaxPhotosPerItem} photos per item";

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

        // Reassigned through the properties on purpose. A field initializer writes the
        // backing field directly, so the generated setter never runs and the collection
        // it created is never handed to OnPhotosChanged / OnItemTagsChanged — which is
        // where the CollectionChanged subscriptions are made. On the edit form the load
        // replaces both collections and hides that; on a *new* item nothing ever does,
        // and the strip would show no primary badge and two dead reorder arrows.
        Photos = [];
        ItemTags = [];
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

        // Handed back by the camera page. Applied in LoadAsync so the add can be
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
        _isAwaitingPhotoSource = false;

        if (_capturedPhotoFileName is string captured)
        {
            _capturedPhotoFileName = null;
            await AddPhotoAsync(captured);
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

            _savedPhotoFileNames = item.Photos.OrderBy(p => p.SortOrder).Select(p => p.FileName).ToList();
            ResetPhotosTo(_savedPhotoFileNames);
        }
        else if (_preselectedLocationId is int preselected)
        {
            SelectedLocation = Locations.FirstOrDefault(l => l.Id == preselected);
        }
    }

    // The collection is replaced wholesale on load and mutated by the add/remove
    // commands, so the limit is tracked from the collection itself rather than from
    // every call site that could change it.
    partial void OnItemTagsChanged(ObservableCollection<Tag>? oldValue, ObservableCollection<Tag> newValue)
    {
        if (oldValue is not null)
            oldValue.CollectionChanged -= OnItemTagsCollectionChanged;

        newValue.CollectionChanged += OnItemTagsCollectionChanged;
        NotifyTagLimitChanged();
    }

    private void OnItemTagsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        NotifyTagLimitChanged();

    private void NotifyTagLimitChanged()
    {
        OnPropertyChanged(nameof(CanAddMoreTags));
        OnPropertyChanged(nameof(IsAtTagLimit));
    }

    // The same arrangement for photos, which three commands mutate and which are
    // restored wholesale on load and on an abandoned edit.
    partial void OnPhotosChanged(ObservableCollection<PhotoSlot>? oldValue, ObservableCollection<PhotoSlot> newValue)
    {
        if (oldValue is not null)
            oldValue.CollectionChanged -= OnPhotosCollectionChanged;

        newValue.CollectionChanged += OnPhotosCollectionChanged;
        NotifyPhotosChanged();
    }

    private void OnPhotosCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        NotifyPhotosChanged();

    private void NotifyPhotosChanged()
    {
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(CanAddMorePhotos));
        OnPropertyChanged(nameof(IsAtPhotoLimit));

        // Which photo is primary is a fact about position — restamped here rather
        // than worked out in the template.
        for (var i = 0; i < Photos.Count; i++)
            Photos[i].IsPrimary = i == 0;
    }

    // Typing filters the tags already in the database down to what is worth offering.
    partial void OnTagInputChanged(string value)
    {
        var input = value.Trim();

        TagSuggestions = input.Length == 0 || IsAtTagLimit
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
            // The entry is disabled at the limit, so this is the backstop for the
            // paths that don't go through it — a suggestion tap, or the pending
            // input swept up on save.
            if (IsAtTagLimit)
            {
                TagInput = string.Empty;
                return;
            }

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
        if (IsAtPhotoLimit)
            return;

        // The page is about to disappear because it is being covered, not left.
        _isAwaitingPhotoSource = true;
        await Shell.Current.GoToAsync("camera");
    }

    [RelayCommand]
    private async Task ChooseFromGalleryAsync()
    {
        if (IsAtPhotoLimit)
            return;

        // The system picker covers the page too, and a covered page is not always
        // distinguishable from a left one. Cleared here rather than in LoadAsync,
        // because a page that was only covered may never come back through it.
        _isAwaitingPhotoSource = true;
        try
        {
            var fileName = await _photoService.PickFromGalleryAsync();
            if (fileName is not null)
                await AddPhotoAsync(fileName);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Error", $"That image could not be imported.\n\n{ex.Message}", "OK");
        }
        finally
        {
            _isAwaitingPhotoSource = false;
        }
    }

    [RelayCommand]
    private void RemovePhoto(PhotoSlot photo)
    {
        // The file stays on disk until the edit is resolved: a removal that is then
        // cancelled has to be able to put the photo back.
        Photos.Remove(photo);
    }

    // Promotion, not a flag: the primary is simply the first of the set, so "show this
    // one in the list" and "put it first" are the same move — and hearting a photo is
    // also how the set is ordered, since it moves that one to the front.
    [RelayCommand]
    private void MakePrimary(PhotoSlot photo)
    {
        var index = Photos.IndexOf(photo);
        if (index > 0)
            Photos.Move(index, 0);
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

        var photoFileNames = Photos.Select(p => p.FileName).ToList();

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
                LocationId = SelectedLocation?.Id
            });

            savedItemId = added.Id;
        }

        // Tags are written separately: the new item needs its id first, and the
        // repository resolves each name to a shared row rather than storing text.
        await _tagRepository.SetItemTagsAsync(savedItemId, ItemTags.Select(t => t.Name));

        // Photos too, for the first of those reasons, and because their order is a
        // property of the set rather than of any one row.
        await _itemRepository.SetItemPhotosAsync(savedItemId, photoFileNames);

        // Only now are the dropped files safe to remove: everything this edit started
        // with or wrote, that the item no longer references.
        await _photoService.DeleteAllAsync(
            _savedPhotoFileNames
                .Concat(_stagedPhotoFileNames)
                .Except(photoFileNames, StringComparer.OrdinalIgnoreCase)
                .ToList());

        _savedPhotoFileNames = photoFileNames;
        _stagedPhotoFileNames.Clear();
        _isCommitted = true;

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        // Just navigate. Photos are reconciled on the way out, so this handler does
        // not have to remember to — and neither does any exit route added later.
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>
    /// Called when the page is leaving. Anything captured during this edit and never
    /// saved is an orphan from here on.
    /// </summary>
    public async Task ReconcilePhotosOnLeaveAsync()
    {
        if (_isAwaitingPhotoSource || _isCommitted)
            return;

        // Nothing was committed, so every file this edit wrote goes — including the
        // ones still on screen, which the item never actually gained.
        await _photoService.DeleteAllAsync(
            _stagedPhotoFileNames
                .Except(_savedPhotoFileNames, StringComparer.OrdinalIgnoreCase)
                .ToList());

        _stagedPhotoFileNames.Clear();
        ResetPhotosTo(_savedPhotoFileNames);
    }

    private async Task AddPhotoAsync(string fileName)
    {
        if (IsAtPhotoLimit)
        {
            // Every control that leads here is disabled at the limit, so this is the
            // backstop for a file that arrived anyway — and it is already written, so
            // dropping it on the floor would leak it until the next launch sweep.
            await _photoService.DeleteAsync(fileName);
            await Shell.Current.DisplayAlertAsync("Too many photos", PhotoLimitMessage, "OK");
            return;
        }

        _stagedPhotoFileNames.Add(fileName);
        Photos.Add(CreateSlot(fileName));
    }

    private void ResetPhotosTo(IEnumerable<string> fileNames) =>
        Photos = new ObservableCollection<PhotoSlot>(fileNames.Select(CreateSlot));

    private PhotoSlot CreateSlot(string fileName) =>
        new(fileName, _photoService.GetFullPath(fileName) is string path ? ImageSource.FromFile(path) : null);
}
