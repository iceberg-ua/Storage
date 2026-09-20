using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Models;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

/// <summary>
/// The search box, its toggles, and the list they produce — the whole of it, so every
/// page that searches composes this one object rather than growing its own copy. Two
/// hand-written copies had already drifted into looking and behaving differently.
/// </summary>
/// <remarks>
/// Browsing is a search with an empty query: the repositories read a blank term as "no
/// filter", so the list a page shows at rest and the list it shows while searching come
/// from the same call and cannot disagree.
/// </remarks>
public partial class SearchViewModel : ObservableObject
{
    private readonly IItemRepository _itemRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly SearchDebounce _debounce = new();
    private readonly bool _locationsByDefault;

    // The location being viewed, or null on a page that shows the whole inventory.
    private int? _locationId;

    // Set while several properties are reset together, so the list is rebuilt once at
    // the end instead of after each one.
    private bool _suspendReload;

    public SearchViewModel(
        IItemRepository itemRepository,
        ILocationRepository locationRepository,
        bool locationsByDefault = false)
    {
        _itemRepository = itemRepository;
        _locationRepository = locationRepository;
        _locationsByDefault = locationsByDefault;
        _searchLocations = locationsByDefault;
    }

    /// <summary>
    /// Narrows this search to one location. Call before the first load; passing null
    /// leaves it searching everything.
    /// </summary>
    public void ScopeTo(int? locationId)
    {
        _locationId = locationId;
        OnPropertyChanged(nameof(IsScopedToLocation));
        OnPropertyChanged(nameof(ShowEverywhereToggle));
    }

    public bool IsScopedToLocation => _locationId is not null;

    // --- The panel ----------------------------------------------------------

    // The toolbar is a fair amount of chrome for a page whose usual job is to show a
    // list, so it stays behind an icon until asked for.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleIcon))]
    [NotifyPropertyChangedFor(nameof(ToggleDescription))]
    private bool _isOpen;

    public string ToggleIcon => IsOpen ? "✖️" : "🔍";

    public string ToggleDescription => IsOpen ? "Close search" : "Search";

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEverythingScope))]
    [NotifyPropertyChangedFor(nameof(IsNameScope))]
    [NotifyPropertyChangedFor(nameof(IsDescriptionScope))]
    [NotifyPropertyChangedFor(nameof(IsTagsScope))]
    private SearchScope _scope = SearchScope.Everything;

    /// <summary>
    /// Off searches only the location being viewed, on searches everything. Shown only
    /// on a page that is scoped to a location; elsewhere there is nothing to widen from.
    /// </summary>
    [ObservableProperty]
    private bool _searchEverywhere;

    /// <summary>
    /// Off searches items, on searches locations. A location has nothing but a name, so
    /// the field-scope strip goes away while this is on.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFieldScope))]
    [NotifyPropertyChangedFor(nameof(ShowItemList))]
    [NotifyPropertyChangedFor(nameof(ShowLocationList))]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    private bool _searchLocations;

    public bool ShowEverywhereToggle => IsScopedToLocation;

    public bool ShowFieldScope => !SearchLocations;

    public string Placeholder => SearchLocations ? "Search locations" : "Search items";

    // The strip binds to these rather than comparing enums in XAML, which has no
    // equality converter and would need one per value.
    public bool IsEverythingScope => Scope == SearchScope.Everything;
    public bool IsNameScope => Scope == SearchScope.Name;
    public bool IsDescriptionScope => Scope == SearchScope.Description;
    public bool IsTagsScope => Scope == SearchScope.Tags;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private int _resultCount;

    public string ResultSummary => ResultCount == 1 ? "1 result" : $"{ResultCount} results";

    // --- The list -----------------------------------------------------------

    [ObservableProperty]
    private ObservableCollection<Item> _items = [];

    [ObservableProperty]
    private ObservableCollection<StorageLocation> _locations = [];

    public bool ShowItemList => !SearchLocations;

    public bool ShowLocationList => SearchLocations;

    [ObservableProperty]
    private string _emptyTitle = "📦 No items yet";

    [ObservableProperty]
    private string _emptyMessage = "Tap ➕ to get started";

    public async Task LoadAsync()
    {
        var term = Text?.Trim() ?? string.Empty;

        IsActive = term.Length > 0;

        // On a page that is not scoped to a location the toggle is moot and the search
        // is over everything either way.
        var within = !IsScopedToLocation || SearchEverywhere ? null : _locationId;

        if (SearchLocations)
        {
            var found = await _locationRepository.SearchAsync(term, within);
            Locations = new ObservableCollection<StorageLocation>(found);
            ResultCount = Locations.Count;
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
            EmptyMessage = IsScopedToLocation
                ? "Nothing is stored inside this one"
                : "Tap ➕ to create your first location";
        }
        else
        {
            EmptyTitle = "📦 No items yet";
            EmptyMessage = IsScopedToLocation
                ? "Tap ➕ to store something here"
                : "Tap ➕ to get started";
        }
    }

    // --- Reacting to the controls -------------------------------------------

    partial void OnTextChanged(string value)
    {
        if (!_suspendReload)
            _debounce.Queue(LoadAsync);
    }

    partial void OnScopeChanged(SearchScope value)
    {
        // The user has already finished typing, so there is nothing to debounce.
        if (!_suspendReload && IsActive)
            _ = LoadAsync();
    }

    // Both toggles change what the list holds even with an empty box, so neither is
    // conditional on a search being under way.
    partial void OnSearchEverywhereChanged(bool value)
    {
        if (!_suspendReload)
            _ = LoadAsync();
    }

    partial void OnSearchLocationsChanged(bool value)
    {
        if (!_suspendReload)
            _ = LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (!IsOpen)
        {
            IsOpen = true;
            return;
        }

        // Closing hides every control that explains why the list looks the way it does,
        // so the page goes back to its resting state rather than staying filtered — or
        // listing the wrong kind of thing — with nothing on screen to say why.
        _suspendReload = true;
        try
        {
            Text = string.Empty;
            Scope = SearchScope.Everything;
            SearchEverywhere = false;
            SearchLocations = _locationsByDefault;
        }
        finally
        {
            _suspendReload = false;
        }

        IsOpen = false;
        await LoadAsync();
    }

    [RelayCommand]
    private void SetScope(SearchScope scope) => Scope = scope;

    // Navigation lives here because the shared row templates bind to it: a row reaches
    // its command through this view model whichever page is showing it.
    [RelayCommand]
    private async Task OpenItemAsync(Item item) =>
        await Shell.Current.GoToAsync($"itemdetail?itemId={item.Id}");

    [RelayCommand]
    private async Task OpenLocationAsync(StorageLocation location) =>
        await Shell.Current.GoToAsync($"locationitems?locationId={location.Id}");
}
