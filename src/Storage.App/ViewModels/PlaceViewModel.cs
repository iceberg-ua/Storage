using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

/// <summary>
/// One location's page, pushed with a "locationId": its photo, where it sits, the
/// locations directly inside it, and the items stored in it. Every level of the tree is
/// its own pushed page, so Back walks up one level the way it does everywhere else.
/// </summary>
/// <remarks>
/// The items list is a <see cref="SearchViewModel"/> scoped to this location, the same
/// object the tabs use, so the search toolbar here behaves exactly as it does there.
/// </remarks>
public partial class PlaceViewModel : ObservableObject, IQueryAttributable
{
    private readonly ILocationRepository _locationRepository;

    private int _locationId;

    public SearchViewModel Search { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    private string? _photoFileName;

    public bool HasPhoto => PhotoFileName is not null;

    /// <summary>The ancestors, top first, e.g. "Garage › Shelf 2"; "Locations" at the top level.</summary>
    [ObservableProperty]
    private string _parentPath = string.Empty;

    /// <summary>"3 items", plus the description when there is one.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubPlaces))]
    private ObservableCollection<LocationSummary> _subPlaces = [];

    public bool HasSubPlaces => SubPlaces.Count > 0;

    public PlaceViewModel(ILocationRepository locationRepository, IItemRepository itemRepository)
    {
        _locationRepository = locationRepository;
        Search = new SearchViewModel(itemRepository, locationRepository);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("locationId", out var locationId) &&
            int.TryParse(Convert.ToString(locationId), out var parsed))
        {
            _locationId = parsed;
            Search.ScopeTo(parsed);
        }
    }

    // Re-read on every appearance, like the item detail page: the edit form pops back
    // here, and a rename, move or new photo should show the moment it does.
    [RelayCommand]
    private async Task LoadAsync()
    {
        // Untracked, so an edit saved through another context is not hidden behind a
        // cached instance.
        var path = await _locationRepository.GetPathAsync(_locationId);
        if (path.Count == 0)
        {
            // Deleted from its own edit form, which then popped back here. The user
            // has just done that on purpose, so there is nothing to announce.
            await Shell.Current.GoToAsync("..");
            return;
        }

        var location = path[^1];
        Name = location.Name;
        Description = location.Description;
        PhotoFileName = location.PhotoFileName;
        ParentPath = path.Count == 1
            ? "Locations"
            : string.Join(" › ", path.Take(path.Count - 1).Select(l => l.Name));

        SubPlaces = new ObservableCollection<LocationSummary>(
            await _locationRepository.GetChildSummariesAsync(_locationId));

        await Search.LoadAsync();

        // At rest the list is exactly what is stored here, so its length is the count.
        // While a search is under way it is the matches, and the count is left as it was.
        if (!Search.IsActive && Search.ShowItemList)
            Summary = FormatSummary(Search.Items.Count, Description);
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
    private Task BackAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private Task EditAsync() => Shell.Current.GoToAsync(Routes.EditLocation(_locationId));

    [RelayCommand]
    private Task AddSubPlaceAsync() =>
        Shell.Current.GoToAsync(Routes.Create(CreateKind.Location, _locationId));

    [RelayCommand]
    private Task NewItemAsync() =>
        Shell.Current.GoToAsync(Routes.Create(CreateKind.Item, _locationId));
}
