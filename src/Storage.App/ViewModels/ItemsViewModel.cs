using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

public partial class ItemsViewModel : ObservableObject
{
    private readonly IItemRepository _itemRepository;

    [ObservableProperty]
    private ObservableCollection<Item> _items = [];

    [ObservableProperty]
    private bool _isLoading;

    public ItemsViewModel(IItemRepository itemRepository)
    {
        _itemRepository = itemRepository;
    }

    [RelayCommand]
    private async Task LoadItemsAsync()
    {
        IsLoading = true;
        try
        {
            var items = await _itemRepository.GetAllAsync();
            Items = new ObservableCollection<Item>(items);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToAddItemAsync()
    {
        await Shell.Current.GoToAsync("additem");
    }
}
