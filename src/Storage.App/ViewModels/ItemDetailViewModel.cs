using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Models;
using Storage.Core.Repositories;
using Storage.Core.Services;

namespace Storage.App.ViewModels;

// One item, pushed with an "itemId". Tapping a row in a list lands here rather than in
// the edit form: looking something up is by far the common action. The one change made
// often, the quantity, is made right here; everything else is behind Edit, and Delete
// sits in the ⋯ menu, a deliberate step away from a stray tap.
public partial class ItemDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly IItemRepository _itemRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IPhotoService _photoService;
    private readonly PlacePicker _placePicker;

    // Everything this page does with the database, and every way off it, goes through
    // here one at a time. The context is shared by the whole app and allows a single
    // operation: a save still running when the user taps Edit would otherwise collide
    // with the edit form's load, and quantity taps that queue up must not overlap.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private int _itemId;

    // Set once the item is gone, so the reload that fires while the page is popping
    // doesn't announce it missing on the way out.
    private bool _isDeleted;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string? _description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDecrease))]
    [NotifyCanExecuteChangedFor(nameof(DecreaseQuantityCommand))]
    private int _quantity = 1;

    public bool CanDecrease => Quantity > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocation))]
    private int? _locationId;

    public bool HasLocation => LocationId is not null;

    /// <summary>"Garage › Shelf 2": where it is, all the way down, since "Shelf 2" alone can be in any room.</summary>
    [ObservableProperty]
    private string _locationPath = string.Empty;

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTags))]
    private ObservableCollection<Tag> _tags = [];

    public bool HasTags => Tags.Count > 0;

    // In display order, primary first — the one the list showed, so the page opens on
    // the photo the user tapped.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhotos))]
    [NotifyPropertyChangedFor(nameof(HasMultiplePhotos))]
    private ObservableCollection<ItemPhoto> _photos = [];

    public bool HasPhotos => Photos.Count > 0;

    // The page dots only earn their space once there is a second photo to swipe to.
    public bool HasMultiplePhotos => Photos.Count > 1;

    /// <summary>
    /// The item was deleted while this page was underneath another one. The page says so
    /// and offers Back rather than popping itself while it is still appearing.
    /// </summary>
    [ObservableProperty]
    private bool _isMissing;

    public ItemDetailViewModel(
        IItemRepository itemRepository,
        ILocationRepository locationRepository,
        IPhotoService photoService,
        PlacePicker placePicker)
    {
        _itemRepository = itemRepository;
        _locationRepository = locationRepository;
        _photoService = photoService;
        _placePicker = placePicker;
    }

    // Shell calls this on the page's BindingContext before the page appears.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("itemId", out var itemId) &&
            int.TryParse(Convert.ToString(itemId), out var parsedItemId))
        {
            _itemId = parsedItemId;
        }
    }

    // Deliberately has no "already loaded" guard, unlike the edit form: there is
    // nothing in progress here to protect, and re-reading on every appearance is what
    // makes the page show an edit the moment the form pops back to it.
    [RelayCommand]
    private Task LoadAsync() => ExclusiveAsync(LoadCoreAsync);

    private async Task LoadCoreAsync()
    {
        if (_isDeleted)
            return;

        try
        {
            var item = await _itemRepository.GetByIdAsync(_itemId);
            IsMissing = item is null;
            if (item is null)
                return;

            Name = item.Name;
            Description = item.Description;
            Quantity = item.Quantity;
            CreatedAt = item.CreatedAt;
            Photos = new ObservableCollection<ItemPhoto>(item.Photos.OrderBy(p => p.SortOrder));
            Tags = new ObservableCollection<Tag>(item.Tags.OrderBy(t => t.Name));

            LocationId = item.LocationId;
            LocationPath = item.LocationId is int locationId
                ? string.Join(" › ", (await _locationRepository.GetPathAsync(locationId)).Select(l => l.Name))
                : string.Empty;
        }
        catch (Exception ex)
        {
            // The page calls this from OnAppearing, an async void handler, where an
            // exception would take the app down.
            await Shell.Current.DisplayAlertAsync(
                "Couldn't open this item", $"Go back and try again.\n\n{ex.Message}".TrimEnd(), "OK");
        }
    }

    // Concurrent, so every tap counts: by default a command ignores taps while its last
    // run is still going, and a quick +++ would add one. The gate queues the writes.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task IncreaseQuantityAsync() => SaveQuantityAsync(Quantity + 1);

    [RelayCommand(CanExecute = nameof(CanDecrease), AllowConcurrentExecutions = true)]
    private Task DecreaseQuantityAsync() => SaveQuantityAsync(Quantity - 1);

    // The number changes at once and the write follows. Each write sends the latest
    // value, so taps that queue up behind a slow one still end on what the user sees.
    private async Task SaveQuantityAsync(int quantity)
    {
        Quantity = quantity;

        // The label's description only refreshes when focus returns to it, so the
        // change is spoken here.
        SemanticScreenReader.Announce($"Quantity {Quantity}");

        await ExclusiveAsync(async () =>
        {
            try
            {
                if (!await _itemRepository.SetQuantityAsync(_itemId, Quantity))
                    IsMissing = true;
            }
            catch (Exception ex)
            {
                // The repository has discarded the failed change, so this reload shows
                // the saved value rather than the one that failed.
                await Shell.Current.DisplayAlertAsync(
                    "Couldn't change the quantity", $"It is back to what was saved.\n\n{ex.Message}".TrimEnd(), "OK");
                await LoadCoreAsync();
            }
        });
    }

    // Every way off the page waits for a pending save, so the page it lands on does
    // not start its load while that save still holds the context.

    // To that location's page, popping back to it when it is already underneath.
    [RelayCommand]
    private Task OpenLocationAsync() => ExclusiveAsync(() => PlaceNavigation.ShowAsync(LocationId));

    [RelayCommand]
    private Task BackAsync() => ExclusiveAsync(() => Shell.Current.GoToAsync(".."));

    // Picked first, outside the gate: the picker is a page of its own, and this page's
    // OnAppearing reload as it closes must not wait on a gate held by its caller.
    [RelayCommand]
    private async Task MoveAsync()
    {
        var pick = await _placePicker.PickAsync(new PlacePickRequest(
            "Move to…",
            HasLocation ? $"{Name} · now in {LocationPath}" : Name,
            LocationId,
            "Move to {0}",
            AllowNone: true));

        if (pick is null || pick.LocationId == LocationId)
            return;

        await ExclusiveAsync(async () =>
        {
            try
            {
                if (!await _itemRepository.MoveAsync(_itemId, pick.LocationId))
                {
                    IsMissing = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlertAsync(
                    "Couldn't move the item", $"It is still where it was.\n\n{ex.Message}".TrimEnd(), "OK");
            }

            await LoadCoreAsync();
        });
    }

    [RelayCommand]
    private Task EditAsync() => ExclusiveAsync(() => Shell.Current.GoToAsync($"edititem?itemId={_itemId}"));

    private async Task ExclusiveAsync(Func<Task> work)
    {
        await _gate.WaitAsync();
        try
        {
            await work();
        }
        finally
        {
            _gate.Release();
        }
    }

    [RelayCommand]
    private async Task ShowActionsAsync()
    {
        const string move = "Move";
        const string delete = "Delete item";

        var choice = await Shell.Current.DisplayActionSheetAsync(Name, "Cancel", delete, move);
        if (choice == move)
            await MoveAsync();
        else if (choice == delete)
            await ExclusiveAsync(DeleteAsync);
    }

    private async Task DeleteAsync()
    {
        var confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Item",
            $"Are you sure you want to delete '{Name}'?",
            "Delete",
            "Cancel");

        if (!confirm)
            return;

        // Read before the delete: the rows go with the item by cascade, and the
        // files they name are ours to remove once that write has succeeded.
        var fileNames = Photos.Select(p => p.FileName).ToList();

        try
        {
            await _itemRepository.DeleteAsync(_itemId);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync(
                "Couldn't delete the item", $"Try again in a moment.\n\n{ex.Message}".TrimEnd(), "OK");
            return;
        }

        await _photoService.DeleteAllAsync(fileNames);

        _isDeleted = true;

        // Back to the list, which reloads as it reappears and so drops the row itself.
        await Shell.Current.GoToAsync("..");
    }
}
