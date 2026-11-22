using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

/// <summary>
/// One step of the path at the top of a location's page or the picker; a null id is the
/// top. In the picker the last one is where you are, shown as plain text rather than a link.
/// </summary>
public sealed record PlaceCrumb(string Name, int? LocationId, bool IsFirst, bool IsLast = false);

/// <summary>
/// One location's page, pushed with a "locationId": its photo, where it sits, the
/// locations directly inside it, and the items stored in it. Every level of the tree is
/// its own pushed page, so Back walks up one level the way it does everywhere else.
/// </summary>
/// <remarks>
/// Search from here opens the Find page with this location as its place filter, which
/// reaches everything inside it too.
/// </remarks>
public partial class PlaceViewModel : ObservableObject, IQueryAttributable
{
    private readonly ILocationRepository _locationRepository;
    private readonly IItemRepository _itemRepository;
    private readonly LocationDeletion _deletion;

    public int LocationId { get; private set; }

    /// <summary>The items stored here itself, newest first; those in sub-locations are behind their chips.</summary>
    [ObservableProperty]
    private ObservableCollection<Item> _items = [];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    private string? _photoFileName;

    public bool HasPhoto => PhotoFileName is not null;

    /// <summary>"Locations › Garage › Shelf 2": everything above this location, each one tappable.</summary>
    [ObservableProperty]
    private ObservableCollection<PlaceCrumb> _crumbs = [];

    /// <summary>"3 items", plus the description when there is one.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private ObservableCollection<LocationSummary> _subPlaces = [];

    /// <summary>
    /// The location was deleted while this page was underneath another one. The page says
    /// so and offers Back rather than popping itself while it is still appearing.
    /// </summary>
    [ObservableProperty]
    private bool _isMissing;

    public PlaceViewModel(
        ILocationRepository locationRepository,
        IItemRepository itemRepository,
        LocationDeletion deletion)
    {
        _locationRepository = locationRepository;
        _itemRepository = itemRepository;
        _deletion = deletion;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("locationId", out var locationId) &&
            int.TryParse(Convert.ToString(locationId), out var parsed))
        {
            LocationId = parsed;
        }
    }

    // Re-read on every appearance, like the item detail page: the edit form pops back
    // here, and a rename, move or new photo should show the moment it does.
    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            // Untracked, so an edit saved through another context is not hidden behind
            // a cached instance.
            var path = await _locationRepository.GetPathAsync(LocationId);
            IsMissing = path.Count == 0;
            if (IsMissing)
                return;

            var location = path[^1];
            Name = location.Name;
            PhotoFileName = location.PhotoFileName;
            Crumbs = new ObservableCollection<PlaceCrumb>(
                path.SkipLast(1)
                    .Select(l => new PlaceCrumb(l.Name, l.Id, IsFirst: false))
                    .Prepend(new PlaceCrumb("Locations", null, IsFirst: true)));

            Items = new ObservableCollection<Item>(await _itemRepository.GetByLocationIdAsync(LocationId));
            Summary = FormatSummary(Items.Count, location.Description);

            SubPlaces = new ObservableCollection<LocationSummary>(
                await _locationRepository.GetChildSummariesAsync(LocationId));
        }
        catch (Exception ex)
        {
            // The page calls this from OnAppearing, an async void handler, where an
            // exception would take the app down.
            await Shell.Current.DisplayAlertAsync(
                "Couldn't open this location", $"Go back and try again.\n\n{ex.Message}".TrimEnd(), "OK");
        }
    }

    private static string FormatSummary(int itemCount, string? description)
    {
        var items = itemCount == 1 ? "1 item" : $"{itemCount} items";
        return string.IsNullOrWhiteSpace(description) ? items : $"{items} · {description}";
    }

    [RelayCommand]
    private Task OpenSubPlaceAsync(LocationSummary subPlace) =>
        Shell.Current.GoToAsync(Routes.Place(subPlace.Location.Id));

    [RelayCommand]
    private Task OpenCrumbAsync(PlaceCrumb crumb) => PlaceNavigation.ShowAsync(crumb.LocationId);

    [RelayCommand]
    private Task BackAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private Task SearchAsync() => Shell.Current.GoToAsync(Routes.Search(LocationId));

    [RelayCommand]
    private Task OpenItemAsync(Item item) => Shell.Current.GoToAsync($"itemdetail?itemId={item.Id}");

    // Edit and Delete share one menu: Delete is rare and should not sit one tap away
    // in the header, next to search.
    [RelayCommand]
    private async Task ShowActionsAsync()
    {
        const string edit = "Edit";
        const string delete = "Delete location";

        var choice = await Shell.Current.DisplayActionSheetAsync(Name, "Cancel", delete, edit);
        if (choice == edit)
        {
            await Shell.Current.GoToAsync(Routes.EditLocation(LocationId));
        }
        else if (choice == delete && await _deletion.ConfirmAndDeleteAsync(LocationId, Name))
        {
            // A tap, not OnAppearing, so the page is settled and can pop itself.
            await Shell.Current.GoToAsync("..");
        }
    }

    [RelayCommand]
    private Task AddSubPlaceAsync() =>
        Shell.Current.GoToAsync(Routes.Create(CreateKind.Location, LocationId));

    [RelayCommand]
    private Task NewItemAsync() =>
        Shell.Current.GoToAsync(Routes.Snap(LocationId));
}
