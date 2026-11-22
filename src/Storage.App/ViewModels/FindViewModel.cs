using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Models;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

/// <summary>An item in the results: the name split around the match, so the match can be highlighted.</summary>
public sealed record FindItemRow(Item Item, string Before, string Match, string After, string Path)
{
    public bool HasPath => Path.Length > 0;
    public bool ShowQuantity => Item.Quantity > 1;
    public string Description => HasPath ? $"{Item.Name}, in {Path}" : Item.Name;
}

/// <summary>A location in the results, with where it sits.</summary>
public sealed record FindPlaceRow(StorageLocation Location, string Before, string Match, string After, string Path)
{
    public bool HasPath => Path.Length > 0;
    public string Description => HasPath ? $"{Location.Name}, in {Path}" : Location.Name;
}

/// <summary>A filter offered while typing: "tag: Car" or "in: Garage".</summary>
public sealed record FindSuggestion(string Label, int? TagId, int? LocationId);

/// <summary>A search the user ran and opened a result from, kept on the device to run again.</summary>
public sealed record RecentSearch(string Text, int? PlaceId, string? PlaceName, int[] TagIds, string[] TagNames)
{
    public string Label => string.Join(" · ",
        new[] { PlaceName is null ? null : $"in: {PlaceName}" }
            .Concat(TagNames.Select(t => $"tag: {t}"))
            .Append(Text.Length > 0 ? Text : null)
            .OfType<string>());
}

/// <summary>
/// The Find page: one search box with filter chips — "in:" a place, which reaches
/// everything inside it, and "tag:" one or more tags, which all have to hold — and the
/// results as Items | Places. It replaces the switches and scope buttons the lists used
/// to carry. With nothing typed and no filter it lists the whole inventory under recent
/// searches and the tags to browse by.
/// </summary>
public partial class FindViewModel : ObservableObject, IQueryAttributable
{
    private const int MaxRecent = 5;
    private const int MaxSuggestions = 3;
    private const string RecentKey = "find.recent";

    private readonly IItemRepository _itemRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly ITagRepository _tagRepository;
    private readonly PlacePicker _placePicker;
    private readonly SearchDebounce _debounce = new();

    private Dictionary<int, StorageLocation> _locationsById = [];
    private List<Tag> _allTags = [];
    private bool _focusRequested;
    private bool _skipDebounce;

    // Each load gets a number; a slower, older one that finishes after a newer one
    // started does not overwrite its results.
    private int _loadVersion;

    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>The "in:" filter, or null for everywhere.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaceFilter))]
    [NotifyPropertyChangedFor(nameof(PlaceFilterLabel))]
    private StorageLocation? _placeFilter;

    public bool HasPlaceFilter => PlaceFilter is not null;

    public string PlaceFilterLabel => PlaceFilter is null ? string.Empty : $"in: {PlaceFilter.Name}";

    public ObservableCollection<Tag> TagFilters { get; } = [];

    /// <summary>The tab chosen; at rest the page shows items whatever it is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowingPlaces))]
    [NotifyPropertyChangedFor(nameof(ShowItemList))]
    [NotifyPropertyChangedFor(nameof(ShowPlaceList))]
    private bool _showingItems = true;

    public bool ShowingPlaces => !ShowingItems;

    public bool ShowItemList => IsIdle || ShowingItems;

    public bool ShowPlaceList => !IsIdle && ShowingPlaces;

    /// <summary>Nothing typed and no filter: recent searches and tags lead, over the whole inventory.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    [NotifyPropertyChangedFor(nameof(ShowItemList))]
    [NotifyPropertyChangedFor(nameof(ShowPlaceList))]
    private bool _isIdle = true;

    public bool IsSearching => !IsIdle;

    [ObservableProperty]
    private ObservableCollection<FindItemRow> _itemResults = [];

    [ObservableProperty]
    private ObservableCollection<FindPlaceRow> _placeResults = [];

    [ObservableProperty]
    private string _itemsTabText = "Items";

    [ObservableProperty]
    private string _placesTabText = "Places";

    /// <summary>"3 more outside Garage — search everywhere", or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutside))]
    private string _outsideText = string.Empty;

    public bool HasOutside => OutsideText.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    private ObservableCollection<FindSuggestion> _suggestions = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecent))]
    private ObservableCollection<RecentSearch> _recent = [];

    public bool HasRecent => Recent.Count > 0;

    [ObservableProperty]
    private ObservableCollection<Tag> _browseTags = [];

    [ObservableProperty]
    private string _emptyText = string.Empty;

    /// <summary>Pushed from a location's page or the Locations tab, so it shows its own Back.</summary>
    [ObservableProperty]
    private bool _isPushed;

    public FindViewModel(
        IItemRepository itemRepository,
        ILocationRepository locationRepository,
        ITagRepository tagRepository,
        PlacePicker placePicker)
    {
        _itemRepository = itemRepository;
        _locationRepository = locationRepository;
        _tagRepository = tagRepository;
        _placePicker = placePicker;
        TagFilters.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTagFilters));
    }

    public bool HasTagFilters => TagFilters.Count > 0;

    // "in" (a location id), "tab=places", "focus=1" and "pushed=1". Cleared once read.
    private int? _pendingPlaceId;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("in", out var place) && int.TryParse(Convert.ToString(place), out var placeId))
            _pendingPlaceId = placeId;
        if (query.TryGetValue("tab", out var tab))
            ShowingItems = Convert.ToString(tab) != "places";
        if (query.ContainsKey("focus"))
            _focusRequested = true;
        if (query.ContainsKey("pushed"))
            IsPushed = true;

        query.Clear();
    }

    /// <summary>True once after the box was asked for; the page then focuses it.</summary>
    public bool TakeFocusRequest()
    {
        var requested = _focusRequested;
        _focusRequested = false;
        return requested;
    }

    // Every appearance: an item edited or added elsewhere should show as it now is.
    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            _locationsById = (await _locationRepository.GetAllAsync()).ToDictionary(l => l.Id);
            _allTags = (await _tagRepository.GetAllAsync())
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            BrowseTags = new ObservableCollection<Tag>(_allTags);
            Recent = new ObservableCollection<RecentSearch>(ReadRecent());

            if (_pendingPlaceId is int id)
            {
                _pendingPlaceId = null;
                PlaceFilter = _locationsById.GetValueOrDefault(id);
            }

            // A filter on a place or tag deleted meanwhile would match nothing, silently.
            if (PlaceFilter is not null && !_locationsById.ContainsKey(PlaceFilter.Id))
                PlaceFilter = null;
            foreach (var gone in TagFilters.Where(t => _allTags.All(a => a.Id != t.Id)).ToList())
                TagFilters.Remove(gone);
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex);
            return;
        }

        await SearchAsync();
    }

    partial void OnTextChanged(string value)
    {
        if (!_skipDebounce)
            _debounce.Queue(SearchAsync);
    }

    private async Task SearchAsync()
    {
        var version = ++_loadVersion;
        var term = Text.Trim();
        var query = new ItemQuery(term, PlaceFilter?.Id, TagFilters.Select(t => t.Id).ToList());

        try
        {
            var items = await _itemRepository.FindAsync(query);

            // Tags belong to items, so a tag filter leaves nothing to say about places.
            var places = TagFilters.Count == 0 && (term.Length > 0 || PlaceFilter is not null)
                ? await _locationRepository.FindAsync(term, PlaceFilter?.Id)
                : Array.Empty<StorageLocation>();

            var outside = PlaceFilter is null
                ? 0
                : await _itemRepository.CountMatchesAsync(query with { WithinLocationId = null }) - items.Count;

            if (version != _loadVersion)
                return;

            IsIdle = term.Length == 0 && PlaceFilter is null && TagFilters.Count == 0;
            ItemResults = new ObservableCollection<FindItemRow>(items.Select(i => ItemRow(i, term)));
            PlaceResults = new ObservableCollection<FindPlaceRow>(places.Select(l => PlaceRow(l, term)));
            ItemsTabText = $"Items · {items.Count}";
            PlacesTabText = $"Places · {places.Count}";
            OutsideText = outside > 0 && PlaceFilter is not null
                ? $"{outside} more outside {PlaceFilter.Name} — search everywhere"
                : string.Empty;
            Suggestions = new ObservableCollection<FindSuggestion>(SuggestionsFor(term));
            EmptyText = IsIdle
                ? "Nothing stored yet. Add something with New item on Home."
                : term.Length > 0 ? $"Nothing matches “{term}”" : "Nothing matches these filters";
        }
        catch (Exception ex)
        {
            // The debounce runs this off any handler; an escaping exception would be lost
            // or end the app, and the user would see a list that silently stopped updating.
            if (version == _loadVersion)
                await ShowFailureAsync(ex);
        }
    }

    private FindItemRow ItemRow(Item item, string term)
    {
        var (before, match, after) = Split(item.Name, term);
        return new FindItemRow(item, before, match, after, PathOf(item.LocationId));
    }

    private FindPlaceRow PlaceRow(StorageLocation location, string term)
    {
        var (before, match, after) = Split(location.Name, term);
        return new FindPlaceRow(location, before, match, after, PathOf(location.ParentId));
    }

    // The name around the first match, for the highlight. A match only in the
    // description or a tag leaves the name whole.
    private static (string Before, string Match, string After) Split(string name, string term)
    {
        var at = term.Length == 0 ? -1 : name.IndexOf(term, StringComparison.CurrentCultureIgnoreCase);
        return at < 0
            ? (name, string.Empty, string.Empty)
            : (name[..at], name.Substring(at, term.Length), name[(at + term.Length)..]);
    }

    private string PathOf(int? locationId)
    {
        var names = new List<string>();
        var seen = new HashSet<int>();
        while (locationId is int id && seen.Add(id) && _locationsById.TryGetValue(id, out var location))
        {
            names.Add(location.Name);
            locationId = location.ParentId;
        }

        names.Reverse();
        return string.Join(" › ", names);
    }

    private IEnumerable<FindSuggestion> SuggestionsFor(string term)
    {
        if (term.Length == 0)
            return [];

        var tags = _allTags
            .Where(t => t.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) && TagFilters.All(f => f.Id != t.Id))
            .Take(MaxSuggestions)
            .Select(t => new FindSuggestion($"tag: {t.Name}", t.Id, null));

        var places = _locationsById.Values
            .Where(l => l.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) && l.Id != PlaceFilter?.Id)
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxSuggestions)
            .Select(l => new FindSuggestion($"in: {l.Name}", null, l.Id));

        return tags.Concat(places);
    }

    // --- Filters ------------------------------------------------------------

    // Picking a suggestion means the typed word was the filter, not a search term.
    [RelayCommand]
    private async Task ApplySuggestionAsync(FindSuggestion suggestion)
    {
        if (suggestion.TagId is int tagId && _allTags.FirstOrDefault(t => t.Id == tagId) is { } tag)
            TagFilters.Add(tag);
        else if (suggestion.LocationId is int locationId)
            PlaceFilter = _locationsById.GetValueOrDefault(locationId);

        _skipDebounce = true;
        Text = string.Empty;
        _skipDebounce = false;
        await SearchAsync();
    }

    [RelayCommand]
    private async Task AddTagFilterAsync(Tag tag)
    {
        if (TagFilters.Any(t => t.Id == tag.Id))
            return;

        TagFilters.Add(tag);
        ShowingItems = true;
        await SearchAsync();
    }

    [RelayCommand]
    private async Task RemoveTagFilterAsync(Tag tag)
    {
        TagFilters.Remove(tag);
        await SearchAsync();
    }

    [RelayCommand]
    private async Task RemovePlaceFilterAsync()
    {
        PlaceFilter = null;
        await SearchAsync();
    }

    // "in: a place": the same picker as Move, with nothing to choose but places.
    [RelayCommand]
    private async Task PickPlaceFilterAsync()
    {
        var pick = await _placePicker.PickAsync(new PlacePickRequest(
            "Search in…", null, PlaceFilter?.Id, "Search in {0}", AllowNone: false));

        if (pick?.LocationId is not int id)
            return;

        // The picker may offer a location added since this page loaded its list.
        if (!_locationsById.ContainsKey(id))
            _locationsById = (await _locationRepository.GetAllAsync()).ToDictionary(l => l.Id);

        PlaceFilter = _locationsById.GetValueOrDefault(id);
        await SearchAsync();
    }

    [RelayCommand]
    private void ShowItems() => ShowingItems = true;

    [RelayCommand]
    private void ShowPlaces() => ShowingItems = false;

    // --- Opening results, and remembering the search that found them --------

    [RelayCommand]
    private async Task OpenItemAsync(FindItemRow row)
    {
        Remember();
        await Shell.Current.GoToAsync($"itemdetail?itemId={row.Item.Id}");
    }

    [RelayCommand]
    private async Task OpenPlaceAsync(FindPlaceRow row)
    {
        Remember();
        await Shell.Current.GoToAsync(Routes.Place(row.Location.Id));
    }

    [RelayCommand]
    private async Task RunRecentAsync(RecentSearch recent)
    {
        PlaceFilter = recent.PlaceId is int id ? _locationsById.GetValueOrDefault(id) : null;
        TagFilters.Clear();
        foreach (var tag in _allTags.Where(t => recent.TagIds.Contains(t.Id)))
            TagFilters.Add(tag);

        // Set without the debounce, which would only run the same search again.
        _skipDebounce = true;
        Text = recent.Text;
        _skipDebounce = false;
        await SearchAsync();
    }

    [RelayCommand]
    private void ClearRecent()
    {
        Recent = [];
        WriteRecent([]);
    }

    [RelayCommand]
    private Task BackAsync() => Shell.Current.GoToAsync("..");

    // Only a search that found what the user opened is worth offering again.
    private void Remember()
    {
        if (IsIdle)
            return;

        var search = new RecentSearch(
            Text.Trim(),
            PlaceFilter?.Id,
            PlaceFilter?.Name,
            TagFilters.Select(t => t.Id).ToArray(),
            TagFilters.Select(t => t.Name).ToArray());

        var list = ReadRecent()
            .Where(r => r.Label != search.Label)
            .Prepend(search)
            .Take(MaxRecent)
            .ToList();

        WriteRecent(list);
        Recent = new ObservableCollection<RecentSearch>(list);
    }

    // On the device only, like everything else in the app. A value that no longer reads
    // (an older format) is treated as no history rather than an error.
    private static List<RecentSearch> ReadRecent()
    {
        try
        {
            var json = Preferences.Default.Get(RecentKey, string.Empty);
            return json.Length == 0 ? [] : JsonSerializer.Deserialize<List<RecentSearch>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void WriteRecent(List<RecentSearch> list) =>
        Preferences.Default.Set(RecentKey, JsonSerializer.Serialize(list));

    private static Task ShowFailureAsync(Exception ex) =>
        Shell.Current.DisplayAlertAsync("Couldn't search", $"Try again in a moment.\n\n{ex.Message}".TrimEnd(), "OK");
}
