using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Models;
using Storage.Core.Repositories;
using Storage.Core.Services;

namespace Storage.App.ViewModels;

// Read-only view of one item, pushed with an "itemId". Tapping a row in the item list
// lands here rather than in the edit form: looking something up is by far the common
// action and editing the rare one. Edit and Delete both hang off this page, which also
// puts the destructive one a deliberate step away from a stray tap on the list.
public partial class ItemDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly IItemRepository _itemRepository;
    private readonly IPhotoService _photoService;

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
    private int _quantity = 1;

    // Falls back to a sentence rather than being hidden: "where is it" is the question
    // this page exists to answer, so "nowhere yet" is an answer worth showing.
    [ObservableProperty]
    private string _locationName = NoLocationText;

    private const string NoLocationText = "No location";

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTags))]
    private ObservableCollection<Tag> _tags = [];

    public bool HasTags => Tags.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    [NotifyPropertyChangedFor(nameof(PhotoSource))]
    private string? _photoFileName;

    public bool HasPhoto => !string.IsNullOrEmpty(PhotoFileName);

    public ImageSource? PhotoSource =>
        _photoService.GetFullPath(PhotoFileName) is string path ? ImageSource.FromFile(path) : null;

    public ItemDetailViewModel(IItemRepository itemRepository, IPhotoService photoService)
    {
        _itemRepository = itemRepository;
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

        var item = await _itemRepository.GetByIdAsync(_itemId);
        if (item is null)
        {
            await Shell.Current.DisplayAlertAsync("Error", "This item no longer exists", "OK");
            await Shell.Current.GoToAsync("..");
            return;
        }

        Name = item.Name;
        Description = item.Description;
        Quantity = item.Quantity;
        LocationName = item.Location?.Name ?? NoLocationText;
        CreatedAt = item.CreatedAt;
        PhotoFileName = item.PhotoPath;
        Tags = new ObservableCollection<Tag>(item.Tags.OrderBy(t => t.Name));
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        await Shell.Current.GoToAsync($"additem?itemId={_itemId}");
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Item",
            $"Are you sure you want to delete '{Name}'?",
            "Delete",
            "Cancel");

        if (!confirm)
            return;

        await _itemRepository.DeleteAsync(_itemId);
        await _photoService.DeleteAsync(PhotoFileName);

        _isDeleted = true;

        // Back to the list, which reloads as it reappears and so drops the row itself.
        await Shell.Current.GoToAsync("..");
    }
}
