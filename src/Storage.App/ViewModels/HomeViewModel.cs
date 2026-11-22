using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

/// <summary>
/// The tab the app opens on: how much is stored, a way into search, what was added
/// lately, and the top-level locations as cards. It answers "where do I start" without
/// the user first choosing between a list of locations and a list of items.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private const int RecentCount = 10;

    private readonly IItemRepository _itemRepository;
    private readonly ILocationRepository _locationRepository;

    /// <summary>"248 items · 14 locations".</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentItems))]
    private ObservableCollection<Item> _recentItems = [];

    public bool HasRecentItems => RecentItems.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaces))]
    [NotifyPropertyChangedFor(nameof(HasNoPlaces))]
    private ObservableCollection<PlaceTile> _places = [];

    public bool HasPlaces => Places.Count > 0;

    /// <summary>Only once loaded, so a user with locations never sees "No locations yet" flash by.</summary>
    public bool HasNoPlaces => IsLoaded && !HasPlaces;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoPlaces))]
    private bool _isLoaded;

    public HomeViewModel(IItemRepository itemRepository, ILocationRepository locationRepository)
    {
        _itemRepository = itemRepository;
        _locationRepository = locationRepository;
    }

    // Every appearance: whatever was added, moved or deleted on another screen shows
    // here the moment the user comes back.
    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var itemCount = await _itemRepository.CountAsync();
            var locationCount = await _locationRepository.CountAsync();
            Summary = $"{Plural(itemCount, "item")} · {Plural(locationCount, "location")}";

            RecentItems = new ObservableCollection<Item>(await _itemRepository.GetRecentAsync(RecentCount));
            Places = new ObservableCollection<PlaceTile>(
                (await _locationRepository.GetTopLevelCardsAsync()).Select(c => new PlaceTile(c)));
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            // Called from OnAppearing, an async void handler, where an exception would
            // end the app.
            await Shell.Current.DisplayAlertAsync(
                "Couldn't load your inventory", $"Try again in a moment.\n\n{ex.Message}".TrimEnd(), "OK");
        }
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    // The box here opens the Find tab, started afresh (the tab keeps its last search
    // otherwise), with the caret in its box.
    [RelayCommand]
    private Task OpenSearchAsync() => Shell.Current.GoToAsync("//find?reset=1&focus=1");

    // Find with nothing typed lists everything stored.
    [RelayCommand]
    private Task SeeAllItemsAsync() => Shell.Current.GoToAsync("//find?reset=1");

    [RelayCommand]
    private Task OpenItemAsync(Item item) => Shell.Current.GoToAsync($"itemdetail?itemId={item.Id}");

    [RelayCommand]
    private Task OpenPlaceAsync(PlaceTile place) => Shell.Current.GoToAsync(Routes.Place(place.LocationId));

    [RelayCommand]
    private Task NewLocationAsync() => Shell.Current.GoToAsync(Routes.Create(CreateKind.Location));

    [RelayCommand]
    private Task NewItemAsync() => Shell.Current.GoToAsync(Routes.Snap());
}
