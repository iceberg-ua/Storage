using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

// Backs the "Items" tab: the whole inventory, wherever it is stored. What is in one
// location is that location's own page. Everything to do with the list itself —
// searching it, browsing it, what it shows when empty — lives in Search, which the
// Locations tab and the location page compose the same way.
public partial class ItemsViewModel : ObservableObject, IQueryAttributable
{
    public SearchViewModel Search { get; }

    [ObservableProperty]
    private bool _isLoading;

    public ItemsViewModel(IItemRepository itemRepository, ILocationRepository locationRepository)
    {
        Search = new SearchViewModel(itemRepository, locationRepository);
    }

    private bool _focusSearchRequested;

    // Home's search box lands here with "search=open" until search has a page of its own.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("search", out var search) && Convert.ToString(search) == "open")
        {
            Search.IsOpen = true;

            // On the first visit this runs while Shell is still building the page, before
            // its search box can take focus, so the page focuses it once it has appeared.
            _focusSearchRequested = true;
        }

        // Cleared once read, so the request is handled once.
        query.Clear();
    }

    /// <summary>True once after Home asked for the search box; the page then focuses it.</summary>
    public bool TakeFocusSearchRequest()
    {
        var requested = _focusSearchRequested;
        _focusSearchRequested = false;
        return requested;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            await Search.LoadAsync();
        }
        catch (Exception ex)
        {
            // Called from OnAppearing, an async void handler, where an exception would end
            // the app. One way here: Home's load still running on the shared database
            // context when the user jumps to this tab.
            await Shell.Current.DisplayAlertAsync(
                "Couldn't load items", $"Try again in a moment.\n\n{ex.Message}".TrimEnd(), "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Not in any location: from here there is no place the user is "in".
    [RelayCommand]
    private Task NewItemAsync() => Shell.Current.GoToAsync(Routes.Create(CreateKind.Item));
}
