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

    // One quantity write at a time: the database context allows a single operation, and
    // a burst of taps would otherwise start a second save before the first finished.
    private readonly SemaphoreSlim _saveGate = new(1, 1);

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
    [NotifyPropertyChangedFor(nameof(HasNoPhotos))]
    [NotifyPropertyChangedFor(nameof(HasMultiplePhotos))]
    private ObservableCollection<ItemPhoto> _photos = [];

    public bool HasPhotos => Photos.Count > 0;

    public bool HasNoPhotos => !HasPhotos;

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
        IPhotoService photoService)
    {
        _itemRepository = itemRepository;
        _locationRepository = locationRepository;
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
    }

    // Deliberately has no "already loaded" guard, unlike the edit form: there is
    // nothing in progress here to protect, and re-reading on every appearance is what
    // makes the page show an edit the moment the form pops back to it.
    [RelayCommand]
    private async Task LoadAsync()
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

    [RelayCommand]
    private Task IncreaseQuantityAsync() => SaveQuantityAsync(Quantity + 1);

    [RelayCommand(CanExecute = nameof(CanDecrease))]
    private Task DecreaseQuantityAsync() => SaveQuantityAsync(Quantity - 1);

    // The number changes at once and the write follows. Each write sends the latest
    // value, so taps that queue up behind a slow one still end on what the user sees.
    private async Task SaveQuantityAsync(int quantity)
    {
        Quantity = quantity;

        await _saveGate.WaitAsync();
        try
        {
            if (!await _itemRepository.SetQuantityAsync(_itemId, Quantity))
                IsMissing = true;
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync(
                "Couldn't change the quantity", $"It is back to what was saved.\n\n{ex.Message}".TrimEnd(), "OK");
            await LoadAsync();
        }
        finally
        {
            _saveGate.Release();
        }
    }

    // To that location's page, popping back to it when it is already underneath.
    [RelayCommand]
    private Task OpenLocationAsync() => PlaceNavigation.ShowAsync(LocationId);

    [RelayCommand]
    private Task BackAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private Task EditAsync() => Shell.Current.GoToAsync($"edititem?itemId={_itemId}");

    // Delete is the only thing in the menu for now; Move joins it with the place picker.
    [RelayCommand]
    private async Task ShowActionsAsync()
    {
        const string delete = "Delete item";

        var choice = await Shell.Current.DisplayActionSheetAsync(Name, "Cancel", delete);
        if (choice == delete)
            await DeleteAsync();
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
