using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

// The Locations tab: every top-level location as a card, with its cover, its
// description on one line and what is inside it. Opening one pushes that location's own
// page. Search opens the Find page on its Places tab.
public partial class LocationsViewModel : ObservableObject
{
    private readonly ILocationRepository _locationRepository;
    private readonly LocationDeletion _deletion;

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

    public LocationsViewModel(ILocationRepository locationRepository, LocationDeletion deletion)
    {
        _locationRepository = locationRepository;
        _deletion = deletion;
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

    private static string FormatSummary(int total, int topLevel)
    {
        var all = total == 1 ? "1 location" : $"{total} locations";
        return total == topLevel ? all : $"{all} · {topLevel} at the top level";
    }

    [RelayCommand]
    private Task SearchAsync() => Shell.Current.GoToAsync(Routes.Search(places: true));

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

}
