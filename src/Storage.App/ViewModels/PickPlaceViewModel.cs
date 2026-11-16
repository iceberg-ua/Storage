using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

/// <summary>One choice in the picker. A null id is "no location".</summary>
public sealed record PickRow(int? LocationId, string Name, string Meta, bool CanOpen, bool IsSelected);

/// <summary>
/// The place picker: one level of the location tree at a time, with the path above it
/// to step back up, and a search box that finds any location by name. Used for moving an
/// item and for choosing an item's location on its form.
/// </summary>
/// <remarks>
/// Reads every location once when it opens and walks the tree in memory: the app holds
/// a few hundred at most, and the user may step up and down several levels.
/// </remarks>
public partial class PickPlaceViewModel : ObservableObject
{
    private readonly ILocationRepository _locationRepository;
    private readonly PlacePicker _picker;

    private PlacePickRequest _request = new("Choose a location", null, null, "Use {0}", AllowNone: true);
    private Dictionary<int, StorageLocation> _byId = [];
    private ILookup<int?, StorageLocation> _children = Array.Empty<StorageLocation>().ToLookup(l => l.ParentId);

    // The level on show; null is the top.
    private int? _levelId;

    // What is chosen. _hasSelection tells "no location chosen" apart from nothing chosen.
    private int? _selectedId;
    private bool _hasSelection;

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
    private string _searchText = string.Empty;

    /// <summary>The path only means something while browsing, not while showing search matches.</summary>
    public bool IsBrowsing => string.IsNullOrWhiteSpace(SearchText);

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
        _request = _picker.Request ?? _request;
        Title = _request.Title;
        Subject = _request.Subject;

        try
        {
            var all = (await _locationRepository.GetAllAsync()).ToList();
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

        // Open on the level holding the current place, so it shows among its siblings.
        var current = _request.CurrentId is int id && _byId.ContainsKey(id) ? id : (int?)null;
        _levelId = current is int c ? _byId[c].ParentId : null;
        _selectedId = current;
        _hasSelection = current is not null || _request.AllowNone;

        Refresh();
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsBrowsing));
        Refresh();
    }

    [RelayCommand]
    private void Select(PickRow row)
    {
        _selectedId = row.LocationId;
        _hasSelection = true;
        Refresh();
    }

    [RelayCommand]
    private void OpenLevel(PickRow row)
    {
        if (row.LocationId is not int id)
            return;

        _levelId = id;
        SearchText = string.Empty;
        Refresh();
    }

    [RelayCommand]
    private void OpenCrumb(PlaceCrumb crumb)
    {
        _levelId = crumb.LocationId;
        Refresh();
    }

    private bool CanConfirm() => _hasSelection;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        // Completed before the pop: leaving the page completes with null otherwise.
        _picker.Complete(new PlacePick(_selectedId));
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        _picker.Complete(null);
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>The page calls this as it goes, however it goes; only the first completion counts.</summary>
    public void Abandon() => _picker.Complete(null);

    private void Refresh()
    {
        Rows = new ObservableCollection<PickRow>(IsBrowsing ? LevelRows() : SearchRows());
        Crumbs = new ObservableCollection<PlaceCrumb>(LevelCrumbs());

        ConfirmText = !_hasSelection
            ? "Choose a location"
            : string.Format(_request.ConfirmFormat, _selectedId is int id ? _byId[id].Name : "no location");
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private IEnumerable<PickRow> LevelRows()
    {
        // The level itself is a choice too: "on the shelf", not in any box on it.
        if (_levelId is int level)
            yield return Row(level, $"{_byId[level].Name} itself", "Not in anything inside it", canOpen: false);
        else if (_request.AllowNone)
            yield return new PickRow(null, "No location", "Not stored anywhere yet", CanOpen: false, IsSelected: _hasSelection && _selectedId is null);

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
            IsSelected: _hasSelection && _selectedId == id);
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
        var path = PathTo(id).Select(l => l.Name).ToList();
        return path.Count == 0 ? null : string.Join(" › ", path);
    }

    // Top first. Stops at a repeat, as the repository does, in case an old cycle remains.
    private List<StorageLocation> PathTo(int? id)
    {
        var path = new List<StorageLocation>();
        var seen = new HashSet<int>();
        while (id is int current && seen.Add(current) && _byId.TryGetValue(current, out var location))
        {
            path.Add(location);
            id = location.ParentId;
        }

        path.Reverse();
        return path;
    }
}
