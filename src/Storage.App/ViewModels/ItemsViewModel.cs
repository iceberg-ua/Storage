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

    // Home's search box lands here with "search=open" until search has a page of its own.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("search", out var search) && Convert.ToString(search) == "open")
            Search.IsOpen = true;

        // Shell keeps a tab's query, so it is cleared once read: otherwise every later
        // visit to the tab would open the toolbar again.
        query.Clear();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            await Search.LoadAsync();
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
