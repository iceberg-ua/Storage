using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

// Backs the "Items" tab: the whole inventory, wherever it is stored. What is in one
// location is that location's own page. Everything to do with the list itself —
// searching it, browsing it, what it shows when empty — lives in Search, which the
// Locations tab and the location page compose the same way.
public partial class ItemsViewModel : ObservableObject
{
    public SearchViewModel Search { get; }

    [ObservableProperty]
    private bool _isLoading;

    public ItemsViewModel(IItemRepository itemRepository, ILocationRepository locationRepository)
    {
        Search = new SearchViewModel(itemRepository, locationRepository);
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
