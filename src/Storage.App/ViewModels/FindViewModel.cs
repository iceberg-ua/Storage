using System.Collections.ObjectModel;
using System.Globalization;
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

/// <summary>A filter offered while typing: "tag: Car" or "in: Shelf · Garage".</summary>
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
/// to carry. With nothing typed and no filter, Items lists the whole inventory under
/// recent searches and the tags to browse by, and Places lists every location.
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
    private readonly LocationDeletion _deletion;
    private readonly SearchDebounce _debounce = new();

    private Dictionary<int, StorageLocation> _locationsById = [];
    private List<Tag> _allTags = [];
    private bool _focusRequested;
    private bool _skipDebounce;
    private int? _pendingPlaceId;

    // Each search gets a number; a slower, older one that finishes after a newer one
    // started does not overwrite its results.
    private int _searchVersion;

    // What the shown results were searched for, so a result opened before the debounce
    // has run the newest text is remembered with the search that actually found it.
    private RecentSearch? _shownSearch;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowingPlaces))]
    [NotifyPropertyChangedFor(nameof(ItemsTabDescription))]
    [NotifyPropertyChangedFor(nameof(PlacesTabDescription))]
    private bool _showingItems = true;

    public bool ShowingPlaces => !ShowingItems;

    /// <summary>Nothing typed and no filter: recent searches and tags lead the whole inventory.</summary>
    [ObservableProperty]
    private bool _isIdle = true;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private ObservableCollection<FindItemRow> _itemResults = [];

    [ObservableProperty]
    private ObservableCollection<FindPlaceRow> _placeResults = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemsTabDescription))]
    private string _itemsTabText = "Items";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlacesTabDescription))]
    private string _placesTabText = "Places";

    // The segment's selection is only colour and weight; a screen reader hears it here.
    public string ItemsTabDescription => ShowingItems ? $"{ItemsTabText}, selected" : ItemsTabText;

    public string PlacesTabDescription => ShowingPlaces ? $"{PlacesTabText}, selected" : PlacesTabText;

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
    [NotifyPropertyChangedFor(nameof(HasBrowseTags))]
    private ObservableCollection<Tag> _browseTags = [];

    public bool HasBrowseTags => BrowseTags.Count > 0;

    [ObservableProperty]
    private string _emptyText = string.Empty;

    [ObservableProperty]
    private string _placesEmptyText = string.Empty;

    /// <summary>Pushed from a location's page or the Locations tab, so it shows its own Back.</summary>
    [ObservableProperty]
    private bool _isPushed;

    public FindViewModel(
        IItemRepository itemRepository,
        ILocationRepository locationRepository,
        ITagRepository tagRepository,
        PlacePicker placePicker,
        LocationDeletion deletion)
    {
        _itemRepository = itemRepository;
        _locationRepository = locationRepository;
        _tagRepository = tagRepository;
        _placePicker = placePicker;
        _deletion = deletion;
    }

    // "reset" starts afresh (the tab page lives on between visits), "in" sets a place,
    // "tab=places" opens on Places, "focus" puts the caret in the box, "pushed" shows Back.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.ContainsKey("reset"))
            Reset();
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

    private void Reset()
    {
        SetTextQuietly(string.Empty);
        PlaceFilter = null;
        TagFilters.Clear();
        ShowingItems = true;
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
            Recent = new ObservableCollection<RecentSearch>(PrunedRecent());

            if (_pendingPlaceId is int id)
            {
                _pendingPlaceId = null;
                PlaceFilter = _locationsById.GetValueOrDefault(id);
                if (PlaceFilter is null)
                    await Shell.Current.DisplayAlertAsync(null, "That location no longer exists, so this searches everywhere.", "OK");
            }

            RefreshFilters();
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex);
            return;
        }

        await SearchAsync();
    }

    // The chips hold the instances read when they were set. Re-read them, so a rename
    // shows, and drop any deleted since: a filter on something gone matches nothing.
    private void RefreshFilters()
    {
        if (PlaceFilter is not null)
            PlaceFilter = _locationsById.GetValueOrDefault(PlaceFilter.Id);

        var fresh = TagFilters
            .Select(t => _allTags.FirstOrDefault(a => a.Id == t.Id))
            .OfType<Tag>()
            .ToList();
        TagFilters.Clear();
        foreach (var tag in fresh)
            TagFilters.Add(tag);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await LoadAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    partial void OnTextChanged(string value)
    {
        if (!_skipDebounce)
            _debounce.Queue(SearchAsync);
    }

    private void SetTextQuietly(string text)
    {
        _skipDebounce = true;
        Text = text;
        _skipDebounce = false;
    }

    private async Task SearchAsync()
    {
        var version = ++_searchVersion;
        var term = Text.Trim();
        var place = PlaceFilter;
        var tags = TagFilters.ToList();
        var query = new ItemQuery(term, place?.Id, tags.Select(t => t.Id).ToList());

        try
        {
            var items = await _itemRepository.FindAsync(query);

            // Tags belong to items, so a tag filter leaves nothing to say about places.
            // With nothing typed and no place, Places lists every location.
            var places = tags.Count == 0
                ? await _locationRepository.FindAsync(term, place?.Id)
                : Array.Empty<StorageLocation>();

            var outside = place is null
                ? 0
                : await _itemRepository.CountMatchesAsync(query with { WithinLocationId = null }) - items.Count;

            if (version != _searchVersion)
                return;

            IsIdle = term.Length == 0 && place is null && tags.Count == 0;
            _shownSearch = IsIdle
                ? null
                : new RecentSearch(term, place?.Id, place?.Name, tags.Select(t => t.Id).ToArray(), tags.Select(t => t.Name).ToArray());

            ItemResults = new ObservableCollection<FindItemRow>(items.Select(i => ItemRow(i, term)));
            PlaceResults = new ObservableCollection<FindPlaceRow>(places.Select(l => PlaceRow(l, term)));
            ItemsTabText = $"Items · {items.Count}";
            PlacesTabText = $"Places · {places.Count}";
            OutsideText = outside > 0 && place is not null
                ? $"{outside} more outside {place.Name} — search everywhere"
                : string.Empty;
            Suggestions = new ObservableCollection<FindSuggestion>(SuggestionsFor(term));
            EmptyText = IsIdle
                ? "Nothing stored yet. Add something with New item."
                : term.Length > 0 ? $"Nothing matches “{term}”" : "Nothing matches these filters";
            PlacesEmptyText = tags.Count > 0
                ? "Tags are on items, not locations"
                : IsIdle ? "No locations yet" : "No location matches";
        }
        catch (Exception ex)
        {
            // The debounce runs this off any handler; an escaping exception would be lost
            // or end the app, and the list would silently stop updating.
            if (version == _searchVersion)
                await ShowFailureAsync(ex);
        }
    }

    private FindItemRow ItemRow(Item item, string term)
    {
        var (before, match, after) = Split(item.Name, term);
        return new FindItemRow(item, before, match, after, LocationPathOf(item.LocationId));
    }

    private FindPlaceRow PlaceRow(StorageLocation location, string term)
    {
        var (before, match, after) = Split(location.Name, term);
        return new FindPlaceRow(location, before, match, after, LocationPathOf(location.ParentId));
    }

    // The name around the first match, for the highlight. The match's own length comes
    // from the comparer: under culture rules it need not be the term's ("co­ffee"
    // matches "coffee", a decomposed "é" matches a precomposed one). A match only in the
    // description or a tag leaves the name whole.
    private static (string Before, string Match, string After) Split(string name, string term)
    {
        if (term.Length == 0)
            return (name, string.Empty, string.Empty);

        var at = CultureInfo.CurrentCulture.CompareInfo.IndexOf(name, term, CompareOptions.IgnoreCase, out var length);
        return at < 0
            ? (name, string.Empty, string.Empty)
            : (name[..at], name.Substring(at, length), name[(at + length)..]);
    }

    private string LocationPathOf(int? locationId)
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

        // With where it sits, since two shelves can share a name.
        var places = _locationsById.Values
            .Where(l => l.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) && l.Id != PlaceFilter?.Id)
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxSuggestions)
            .Select(l =>
            {
                var parent = LocationPathOf(l.ParentId);
                return new FindSuggestion(parent.Length == 0 ? $"in: {l.Name}" : $"in: {l.Name} · {parent}", null, l.Id);
            });

        return tags.Concat(places);
    }

    // --- Filters ------------------------------------------------------------

    // Picking a suggestion means the typed word was the filter, not a search term.
    [RelayCommand]
    private async Task ApplySuggestionAsync(FindSuggestion suggestion)
    {
        if (suggestion.TagId is int tagId && _allTags.FirstOrDefault(t => t.Id == tagId) is { } tag)
        {
            TagFilters.Add(tag);

            // Tags narrow items only; Places would just be empty.
            ShowingItems = true;
        }
        else if (suggestion.LocationId is int locationId)
        {
            PlaceFilter = _locationsById.GetValueOrDefault(locationId);
        }

        SetTextQuietly(string.Empty);
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

    // The swipe on a location in the results, as on the Locations tab.
    [RelayCommand]
    private async Task DeletePlaceAsync(FindPlaceRow row)
    {
        if (await _deletion.ConfirmAndDeleteAsync(row.Location.Id, row.Location.Name))
            await LoadAsync();
    }

    [RelayCommand]
    private Task NewItemAsync() =>
        Shell.Current.GoToAsync(Routes.Snap(PlaceFilter?.Id));

    [RelayCommand]
    private async Task RunRecentAsync(RecentSearch recent)
    {
        PlaceFilter = recent.PlaceId is int id ? _locationsById.GetValueOrDefault(id) : null;
        TagFilters.Clear();
        foreach (var tag in _allTags.Where(t => recent.TagIds.Contains(t.Id)))
            TagFilters.Add(tag);

        // Set without the debounce, which would only run the same search again.
        SetTextQuietly(recent.Text);
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
        if (_shownSearch is not { } search)
            return;

        var list = ReadRecent()
            .Where(r => r.Label != search.Label)
            .Prepend(search)
            .Take(MaxRecent)
            .ToList();

        WriteRecent(list);
        Recent = new ObservableCollection<RecentSearch>(list);
    }

    // A recent search whose place or tag has been deleted would quietly run as a
    // different search under a label that still names it; it is dropped instead. One
    // whose place or tags were renamed is relabelled.
    private List<RecentSearch> PrunedRecent()
    {
        var kept = new List<RecentSearch>();
        foreach (var recent in ReadRecent())
        {
            var place = recent.PlaceId is int id ? _locationsById.GetValueOrDefault(id) : null;
            var tags = _allTags.Where(t => recent.TagIds.Contains(t.Id)).ToList();
            if ((recent.PlaceId is not null && place is null) || tags.Count != recent.TagIds.Distinct().Count())
                continue;

            kept.Add(recent with { PlaceName = place?.Name, TagNames = tags.Select(t => t.Name).ToArray() });
        }

        WriteRecent(kept);
        return kept;
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
