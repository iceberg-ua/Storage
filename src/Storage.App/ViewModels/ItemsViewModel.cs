using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

// Backs the "Items" tab: everything at the top level, or what is stored in the current
// location once one is opened. Everything to do with the list itself — searching it,
// browsing it, what it shows when empty — lives in Search, which the Locations page
// composes the same way.
public partial class ItemsViewModel : ObservableObject
{
    public LocationContext Context { get; }

    public SearchViewModel Search { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLocationView;

    public ItemsViewModel(
        IItemRepository itemRepository,
        ILocationRepository locationRepository,
        LocationContext locationContext)
    {
        Context = locationContext;
        Search = new SearchViewModel(itemRepository, locationRepository, locationContext);
    }

    // Followed only while the page is on screen: the context is a singleton, so a
    // subscription left in place would keep this view model alive for good. A tab
    // that was hidden when the path changed catches up in RefreshAsync on appearing.
    public void Attach()
    {
        Context.Changed -= OnContextChanged;
        Context.Changed += OnContextChanged;
    }

    public void Detach() => Context.Changed -= OnContextChanged;

    private async void OnContextChanged(object? sender, EventArgs e) => await LoadAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Picks up a rename, move or delete of the current location made on the edit
        // screen; if that moved the user, Changed has already reloaded the list.
        var before = Context.CurrentId;
        await Context.RefreshAsync();

        if (Context.CurrentId == before)
            await LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            IsLocationView = Context.IsInside;
            Search.ScopeTo(Context.CurrentId);
            await Search.LoadAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToAddItemAsync()
    {
        // Inside a location, new items belong to it by default.
        var route = Context.CurrentId is int id ? $"additem?locationId={id}" : "additem";
        await Shell.Current.GoToAsync(route);
    }

    [RelayCommand]
    private async Task EditLocationAsync()
    {
        if (Context.CurrentId is not int id)
            return;

        await Shell.Current.GoToAsync($"addlocation?locationId={id}");
    }
}
