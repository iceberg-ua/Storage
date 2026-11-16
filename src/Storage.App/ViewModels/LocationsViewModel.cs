using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Repositories;
using StorageLocation = Storage.Core.Models.Location;

namespace Storage.App.ViewModels;

// The Locations tab: every top-level location as a card, with its cover, its
// description on one line and what is inside it. Opening one pushes that location's own
// page. The search toolbar replaces the cards while it is open, with the same toggles
// as everywhere else, so finding something here does not need another tab.
public partial class LocationsViewModel : ObservableObject
{
    private readonly ILocationRepository _locationRepository;
    private readonly LocationDeletion _deletion;

    public SearchViewModel Search { get; }

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>"14 locations · 5 at the top level".</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PlaceTile> _places = [];

    /// <summary>
    /// Set after the first load, so the empty state does not flash up on every cold
    /// start before the cards arrive.
    /// </summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>The cards show while the search toolbar is closed; its results while it is open.</summary>
    public bool IsBrowsing => !Search.IsOpen;

    public bool IsSearching => Search.IsOpen;

    public LocationsViewModel(
        ILocationRepository locationRepository,
        IItemRepository itemRepository,
        LocationDeletion deletion)
    {
        _locationRepository = locationRepository;
        _deletion = deletion;

        // Locations first, since that is what this page is for; the toggle still
        // reaches items without leaving the page.
        Search = new SearchViewModel(itemRepository, locationRepository, locationsByDefault: true);
        Search.PropertyChanged += OnSearchPropertyChanged;
    }

    // Opening the toolbar swaps the cards for its results, which have not been loaded
    // yet. Closing it swaps the cards back, reloaded: a location may have been deleted
    // from the results meanwhile, and its card and the counts would still show it.
    private async void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SearchViewModel.IsOpen))
            return;

        OnPropertyChanged(nameof(IsBrowsing));
        OnPropertyChanged(nameof(IsSearching));
        if (Search.IsOpen)
            await LoadSearchAsync();
        else
            await LoadLocationsAsync();
    }

    [RelayCommand]
    private async Task LoadLocationsAsync()
    {
        IsLoading = true;
        try
        {
            var cards = await _locationRepository.GetTopLevelCardsAsync();
            Places = new ObservableCollection<PlaceTile>(cards.Select(c => new PlaceTile(c)));
            Summary = FormatSummary(await _locationRepository.CountAsync(), cards.Count);

            if (Search.IsOpen)
                await Search.LoadAsync();

            IsLoaded = true;
        }
        catch (Exception ex)
        {
            // Called from OnAppearing, an async void handler, where an exception would
            // end the app.
            await Shell.Current.DisplayAlertAsync(
                "Couldn't load locations", $"Try again in a moment.\n\n{ex.Message}".TrimEnd(), "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSearchAsync()
    {
        try
        {
            await Search.LoadAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync(
                "Couldn't search", $"Try again in a moment.\n\n{ex.Message}".TrimEnd(), "OK");
        }
    }

    private static string FormatSummary(int total, int topLevel)
    {
        var all = total == 1 ? "1 location" : $"{total} locations";
        return total == topLevel ? all : $"{all} · {topLevel} at the top level";
    }

    [RelayCommand]
    private Task OpenPlaceAsync(PlaceTile place) => Shell.Current.GoToAsync(Routes.Place(place.LocationId));

    // The card's swipe. Reloads afterwards, so the summary's counts follow.
    [RelayCommand]
    private async Task DeletePlaceAsync(PlaceTile place)
    {
        if (await _deletion.ConfirmAndDeleteAsync(place.LocationId, place.Name))
            await LoadLocationsAsync();
    }

    [RelayCommand]
    private Task NavigateToCreateAsync() =>
        Shell.Current.GoToAsync(Routes.Create(CreateKind.Location));

    // A new item at the top level; inside a location, its own page adds one there.
    [RelayCommand]
    private Task NewItemAsync() => Shell.Current.GoToAsync(Routes.Create(CreateKind.Item));

    // The swipe on a search result.
    [RelayCommand]
    private async Task DeleteLocationAsync(StorageLocation location)
    {
        if (await _deletion.ConfirmAndDeleteAsync(location.Id, location.Name))
            Search.Locations.Remove(location);
    }
}
