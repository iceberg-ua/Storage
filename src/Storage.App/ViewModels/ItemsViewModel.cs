using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Models;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// Backs the "Items" tab and, when Shell passes a "locationId", the same page scoped
// to a single location (pushed from the Locations list). In the scoped view the page
// gains a second tab listing that location's child locations.
public partial class ItemsViewModel : ObservableObject, IQueryAttributable
{
    private readonly IItemRepository _itemRepository;
    private readonly ILocationRepository _locationRepository;

    private int? _locationId;

    // Cancels the previous keystroke's pending search, so typing "hammer" costs one
    // query rather than six.
    private CancellationTokenSource? _searchDebounce;

    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    [ObservableProperty]
    private ObservableCollection<Item> _items = [];

    [ObservableProperty]
    private ObservableCollection<StorageLocation> _childLocations = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLocationView;

    // The "Search locations" toggle is the only thing that decides what this page lists,
    // whether or not anything has been typed: with an empty box it simply means "every
    // location here". That subsumes what the old Items/Locations tab strip did, so the
    // strip is gone — two controls answering the same question is what let the list show
    // items while the toggle said locations.
    public bool ShowItemList => !SearchLocations;

    public bool ShowLocationList => SearchLocations;

    [ObservableProperty]
    private string _pageTitle = "My Items";

    [ObservableProperty]
    private string _emptyMessage = "Tap ➕ to get started";

    [ObservableProperty]
    private string _emptyTitle = "📦 No items yet";

    // --- Search -------------------------------------------------------------

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEverythingScope))]
    [NotifyPropertyChangedFor(nameof(IsNameScope))]
    [NotifyPropertyChangedFor(nameof(IsDescriptionScope))]
    [NotifyPropertyChangedFor(nameof(IsTagsScope))]
    private SearchScope _scope = SearchScope.Everything;

    /// <summary>
    /// Off searches only the location being viewed, on searches the whole inventory.
    /// Only shown in the location view; the Items tab is global already.
    /// </summary>
    [ObservableProperty]
    private bool _searchEverywhere;

    /// <summary>
    /// Off searches items, on searches locations by name. A location has nothing but a
    /// name, so the field-scope strip goes away while this is on.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFieldScope))]
    [NotifyPropertyChangedFor(nameof(ShowItemList))]
    [NotifyPropertyChangedFor(nameof(ShowLocationList))]
    [NotifyPropertyChangedFor(nameof(SearchPlaceholder))]
    private bool _searchLocations;

    public bool ShowFieldScope => !SearchLocations;

    public string SearchPlaceholder => SearchLocations ? "Search locations" : "Search items";

    // The segmented strip binds to these rather than comparing enums in XAML, which
    // has no equality converter and would need one per value.
    public bool IsEverythingScope => Scope == SearchScope.Everything;
    public bool IsNameScope => Scope == SearchScope.Name;
    public bool IsDescriptionScope => Scope == SearchScope.Description;
    public bool IsTagsScope => Scope == SearchScope.Tags;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private bool _isSearchActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private int _resultCount;

    public string ResultSummary => ResultCount == 1 ? "1 result" : $"{ResultCount} results";

    public ItemsViewModel(
        IItemRepository itemRepository,
        ILocationRepository locationRepository)
    {
        _itemRepository = itemRepository;
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

        IsLocationView = _locationId is not null;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            if (_locationId is int id)
            {
                // Re-read the location each time so a rename made on the edit screen
                // is reflected when we come back to this page.
                var location = await _locationRepository.GetByIdAsync(id);
                PageTitle = location?.Name ?? "Location";
            }

            await LoadListAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Fills whichever list the "Search locations" toggle has on show. Browsing is just
    /// a search with an empty query — the repositories treat a blank term as "no
    /// filter" — so there is one path here rather than two that can disagree.
    /// </summary>
    private async Task LoadListAsync()
    {
        var term = SearchText?.Trim() ?? string.Empty;

        IsSearchActive = term.Length > 0;

        // In the Items tab there is no location to narrow to, so the toggle is moot
        // and the search is global either way.
        var within = !IsLocationView || SearchEverywhere ? null : _locationId;

        if (SearchLocations)
        {
            var found = await _locationRepository.SearchAsync(term, within);
            ChildLocations = new ObservableCollection<StorageLocation>(found);
            ResultCount = ChildLocations.Count;
        }
        else
        {
            var found = await _itemRepository.SearchAsync(term, Scope, within);
            Items = new ObservableCollection<Item>(found);
            ResultCount = Items.Count;
        }

        SetEmptyState(term);
    }

    // Three different nothings, and saying which one it is saves the user guessing:
    // a query that matched nothing, an empty shelf, or a location with nothing inside.
    private void SetEmptyState(string term)
    {
        if (term.Length > 0)
        {
            EmptyTitle = "🔍 No matches";
            EmptyMessage = $"Nothing here matches “{term}”";
        }
        else if (SearchLocations)
        {
            EmptyTitle = "📍 No locations";
            EmptyMessage = IsLocationView
                ? "Nothing is stored inside this one"
                : "Add one from the Locations tab";
        }
        else
        {
            EmptyTitle = "📦 No items yet";
            EmptyMessage = IsLocationView
                ? "Tap ➕ to store something here"
                : "Tap ➕ to get started";
        }
    }

    // Generated by the [ObservableProperty] on SearchText; typing re-runs the query.
    partial void OnSearchTextChanged(string value) => QueueSearch();

    partial void OnScopeChanged(SearchScope value)
    {
        // Switching field mid-search should re-query at once — the user has already
        // finished typing, so there is nothing to debounce.
        if (IsSearchActive)
            _ = LoadListAsync();
    }

    // Both toggles change what the list holds even with an empty box, so neither is
    // conditional on a search being under way.
    partial void OnSearchEverywhereChanged(bool value) => _ = LoadListAsync();

    partial void OnSearchLocationsChanged(bool value) => _ = LoadListAsync();

    private void QueueSearch()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();

        var cts = new CancellationTokenSource();
        _searchDebounce = cts;

        _ = DebounceAsync(cts.Token);

        async Task DebounceAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(DebounceDelay, token);
            }
            catch (TaskCanceledException)
            {
                // Superseded by a later keystroke; that one owns the query now.
                return;
            }

            await LoadListAsync();
        }
    }

    [RelayCommand]
    private void SetScope(SearchScope scope) => Scope = scope;

    [RelayCommand]
    private async Task NavigateToAddItemAsync()
    {
        // In a location view, new items belong to that location by default.
        var route = _locationId is int id ? $"additem?locationId={id}" : "additem";
        await Shell.Current.GoToAsync(route);
    }

    [RelayCommand]
    private async Task EditLocationAsync()
    {
        if (_locationId is not int id)
            return;

        await Shell.Current.GoToAsync($"addlocation?locationId={id}");
    }

    // Drill further down the hierarchy into a child location.
    [RelayCommand]
    private async Task OpenChildLocationAsync(StorageLocation location)
    {
        await Shell.Current.GoToAsync($"locationitems?locationId={location.Id}");
    }

    // Tapping an item opens it for reading. Editing and deleting live on that screen,
    // mirroring how tapping a location drills into it rather than opening its form.
    [RelayCommand]
    private async Task OpenItemAsync(Item item)
    {
        await Shell.Current.GoToAsync($"itemdetail?itemId={item.Id}");
    }
}
