using System.ComponentModel;
using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class ItemsPage : ContentPage
{
    private readonly ItemsViewModel _viewModel;

    public ItemsPage(ItemsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        _viewModel.Search.PropertyChanged += OnSearchPropertyChanged;
    }

    // Opening the toolbar and then having to tap the box to type in it is a wasted tap,
    // so the caret goes there with it — and the keyboard comes up on its own.
    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.IsOpen) && _viewModel.Search.IsOpen)
            Toolbar.Focus();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Asked for by Home's search box. Done here, once the page is on screen, since a
        // box that is not attached yet ignores Focus.
        if (_viewModel.TakeFocusSearchRequest())
            Toolbar.Focus();

        await _viewModel.RefreshCommand.ExecuteAsync(null);
    }
}
