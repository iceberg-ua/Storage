using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

/// <summary>
/// One choice in the picker. A null id is "no location". Selection flips in place, so
/// choosing a row does not rebuild the list and jump it back to the top.
/// </summary>
public sealed partial class PickRow : ObservableObject
{
    public PickRow(int? locationId, string name, string meta, bool canOpen, bool isSelected)
    {
        LocationId = locationId;
        Name = name;
        Meta = meta;
        CanOpen = canOpen;
        _isSelected = isSelected;
    }

    public int? LocationId { get; }
    public string Name { get; }
    public string Meta { get; }
    public bool CanOpen { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private bool _isSelected;

    /// <summary>What a screen reader says: the name, whether it is chosen, and the line under it.</summary>
    public string Description => IsSelected ? $"{Name}, selected. {Meta}" : $"{Name}. {Meta}";
}

/// <summary>
/// The place picker: one level of the location tree at a time, with the path above it
/// to step back up, and a search box that finds any location by name. Used for moving an
/// item, for choosing an item's location on its form, and for choosing the location a
/// location sits inside.
/// </summary>
/// <remarks>
/// Reads every location once when it opens and walks the tree in memory: the app holds
/// a few hundred at most, and the user may step up and down several levels.
/// </remarks>
public partial class PickPlaceViewModel : ObservableObject
{
    private readonly ILocationRepository _locationRepository;
    private readonly PlacePicker _picker;

    // The opening this page answers, taken on the first load and kept: the page may
    // appear again, and must not pick up some later request instead.
    private PlacePickSession? _session;
    private PlacePickRequest _request = new("Choose a location", null, null, "Use {0}", AllowNone: true);

    private Dictionary<int, StorageLocation> _byId = [];
    private ILookup<int?, StorageLocation> _children = Array.Empty<StorageLocation>().ToLookup(l => l.ParentId);
    private bool _isLoaded;

    // The level on show; null is the top.
    private int? _levelId;

    // What is chosen. _hasSelection tells "no location chosen" apart from nothing chosen.
    private int? _selectedId;
    private bool _hasSelection;

    /// <summary>Set by Confirm or Close, so a second one — or Back — cannot pop the page underneath too.</summary>
    public bool IsClosing { get; private set; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubject))]
    private string? _subject;

    public bool HasSubject => !string.IsNullOrEmpty(Subject);

    [ObservableProperty]
    private ObservableCollection<PlaceCrumb> _crumbs = [];

    [ObservableProperty]
    private ObservableCollection<PickRow> _rows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowsing))]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private string _searchText = string.Empty;

    /// <summary>The path only means something while browsing, not while showing search matches.</summary>
    public bool IsBrowsing => string.IsNullOrWhiteSpace(SearchText);

    public string EmptyText => IsBrowsing ? "No locations yet" : "No location matches that name";

    [ObservableProperty]
    private string _confirmText = "Choose a location";

    public PickPlaceViewModel(ILocationRepository locationRepository, PlacePicker picker)
    {
        _locationRepository = locationRepository;
        _picker = picker;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_isLoaded)
            return;

        _session ??= _picker.TakeOpening();
        _request = _session?.Request ?? _request;
        Title = _request.Title;
        Subject = _request.Subject;

        try
        {
            // Left out entirely, not shown greyed: a location cannot go inside itself or
            // anything inside it, and the request lists all of those.
            var excluded = _request.ExcludedIds ?? new HashSet<int>();
            var all = (await _locationRepository.GetAllAsync())
                .Where(l => !excluded.Contains(l.Id))
                .ToList();
            _byId = all.ToDictionary(l => l.Id);
            _children = all
                .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToLookup(l => l.ParentId);
        }
        catch (Exception ex)
        {
            // Called from OnAppearing, an async void handler, where an exception would
            // end the app.
            await Shell.Current.DisplayAlertAsync(
                "Couldn't load locations", $"Close this and try again.\n\n{ex.Message}".TrimEnd(), "OK");
            return;
        }

        _isLoaded = true;

        // Open on the level holding the current place, so it shows among its siblings.
        var current = _request.CurrentId is int id && _byId.ContainsKey(id) ? id : (int?)null;
        _levelId = current is int c ? _byId[c].ParentId : null;
        _selectedId = current;
        _hasSelection = current is not null || _request.AllowNone;

        Rebuild();
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    [RelayCommand]
    private void Select(PickRow row)
    {
        _selectedId = row.LocationId;
        _hasSelection = true;

        foreach (var r in Rows)
            r.IsSelected = r == row;

        UpdateConfirm();
    }

    [RelayCommand]
    private void OpenLevel(PickRow row)
    {
        if (row.LocationId is not int id)
            return;

        _levelId = id;
        if (IsBrowsing)
            Rebuild();
        else
            SearchText = string.Empty; // rebuilds on the new level
    }

    [RelayCommand]
    private void OpenCrumb(PlaceCrumb crumb)
    {
        _levelId = crumb.LocationId;
        Rebuild();
    }

    // Choosing where it already is would do nothing, so the button says so by being off.
    private bool CanConfirm() => _hasSelection && _selectedId != _request.CurrentId;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (IsClosing)
            return;

        IsClosing = true;

        // Answered before the pop: leaving the page answers null otherwise.
        _session?.Complete(new PlacePick(_selectedId));
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (IsClosing)
            return;

        IsClosing = true;
        _session?.Complete(null);
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>The page calls this as it goes, however it goes; after a confirm it does nothing.</summary>
    public void Abandon() => _session?.Complete(null);

    // The list itself only changes with the level or the search; selection flips rows in place.
    private void Rebuild()
    {
        if (!_isLoaded)
            return;

        Rows = new ObservableCollection<PickRow>(IsBrowsing ? LevelRows() : SearchRows());
        Crumbs = new ObservableCollection<PlaceCrumb>(LevelCrumbs());
        UpdateConfirm();
    }

    private void UpdateConfirm()
    {
        if (!_hasSelection)
            ConfirmText = "Choose a location";
        else if (_selectedId == _request.CurrentId)
            ConfirmText = _selectedId is null ? $"Already in {_request.NoneInSentence}" : "Already here";
        else
            ConfirmText = string.Format(_request.ConfirmFormat, _selectedId is int id ? _byId[id].Name : _request.NoneInSentence);

        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private IEnumerable<PickRow> LevelRows()
    {
        // The level itself is a choice too: "on the shelf", not in any box on it.
        if (_levelId is int level)
            yield return Row(level, $"{_byId[level].Name} itself", "Not in anything inside it", canOpen: false);
        else if (_request.AllowNone)
            yield return new PickRow(null, _request.NoneName, _request.NoneMeta, canOpen: false,
                isSelected: _hasSelection && _selectedId is null);

        foreach (var child in _children[_levelId])
            yield return Row(child.Id, child.Name, ChildMeta(child.Id), canOpen: _children[child.Id].Any());
    }

    private IEnumerable<PickRow> SearchRows()
    {
        var term = SearchText.Trim();
        return _byId.Values
            .Where(l => l.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => Row(l.Id, l.Name, PathOf(l.ParentId) ?? "Top level", canOpen: _children[l.Id].Any()));
    }

    private PickRow Row(int id, string name, string meta, bool canOpen)
    {
        var isCurrent = id == _request.CurrentId;
        return new PickRow(
            id,
            name,
            isCurrent ? $"Now here · {meta}" : meta,
            canOpen,
            isSelected: _hasSelection && _selectedId == id);
    }

    private string ChildMeta(int id)
    {
        var inside = _children[id].Count();
        return inside switch
        {
            0 => "Nothing inside",
            1 => "1 location inside",
            _ => $"{inside} locations inside"
        };
    }

    private IEnumerable<PlaceCrumb> LevelCrumbs()
    {
        yield return new PlaceCrumb("All locations", null, IsFirst: true);
        foreach (var location in PathTo(_levelId))
            yield return new PlaceCrumb(location.Name, location.Id, IsFirst: false);
    }

    private string? PathOf(int? id)
    {
        var path = LocationPath.Of(id, _byId);
        return path.Length == 0 ? null : path;
    }

    private List<StorageLocation> PathTo(int? id) => LocationPath.Chain(id, _byId);
}
