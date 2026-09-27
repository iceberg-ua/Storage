using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.Services;

/// <summary>
/// The location the user is "inside", shared by the Locations and Items tabs: Items
/// shows what is stored there, Locations shows what is nested in it. Opening a location
/// sets it; the header's back arrow, its path and the Back button walk it back up.
/// </summary>
/// <remarks>
/// Held here rather than passed as a route parameter because it belongs to neither tab:
/// a query parameter reaches only the page being navigated to, so the other tab would
/// not know where the user is.
/// </remarks>
public partial class LocationContext : ObservableObject
{
    private readonly ILocationRepository _locationRepository;

    public LocationContext(ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
    }

    /// <summary>
    /// Raised when the current location changes — not on a rename, which changes only
    /// what the header says, not which list the tabs should show.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>From the top-level location down to the current one; empty at the top.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current))]
    [NotifyPropertyChangedFor(nameof(IsInside))]
    [NotifyPropertyChangedFor(nameof(IsAtTop))]
    private IReadOnlyList<StorageLocation> _path = [];

    public StorageLocation? Current => Path.Count > 0 ? Path[^1] : null;

    public int? CurrentId => Current?.Id;

    public bool IsInside => Path.Count > 0;

    public bool IsAtTop => !IsInside;

    public async Task EnterAsync(int locationId) =>
        SetPath(await _locationRepository.GetPathAsync(locationId));

    /// <summary>Steps out to the parent. Returns false when already at the top.</summary>
    public bool GoUp()
    {
        if (!IsInside)
            return false;

        SetPath(Path.Take(Path.Count - 1).ToList());
        return true;
    }

    /// <summary>
    /// Re-reads the path, so a rename or a move made on the edit screen shows in the
    /// header. If the current location was deleted meanwhile, falls back to the nearest
    /// ancestor that still exists.
    /// </summary>
    public async Task RefreshAsync()
    {
        for (var i = Path.Count - 1; i >= 0; i--)
        {
            var path = await _locationRepository.GetPathAsync(Path[i].Id);
            if (path.Count > 0)
            {
                SetPath(path);
                return;
            }
        }

        SetPath([]);
    }

    // Every way back out lands on the Locations tab: opening a location moved the user
    // from there to Items, so going back reverses that and shows the level they are
    // returning to, with the location they just left in it.

    // The title's back arrow, and the Android Back button.
    [RelayCommand]
    private async Task BackAsync()
    {
        if (GoUp())
            await ShowLocationsAsync();
    }

    // A path segment: jump straight to that level.
    [RelayCommand]
    private async Task JumpToAsync(StorageLocation location)
    {
        var index = Path.ToList().FindIndex(l => l.Id == location.Id);
        SetPath(Path.Take(index + 1).ToList());
        await ShowLocationsAsync();
    }

    // The home icon: straight to the top.
    [RelayCommand]
    private async Task LeaveAsync()
    {
        SetPath([]);
        await ShowLocationsAsync();
    }

    private static Task ShowLocationsAsync() => Shell.Current.GoToAsync("//locations");

    private void SetPath(IReadOnlyList<StorageLocation> path)
    {
        var previousId = CurrentId;
        Path = path;

        if (CurrentId != previousId)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
