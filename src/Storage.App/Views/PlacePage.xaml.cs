using System.ComponentModel;
using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class PlacePage : ContentPage
{
    private readonly PlaceViewModel _viewModel;

    public PlacePage(PlaceViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        _viewModel.Search.PropertyChanged += OnSearchPropertyChanged;
    }

    // Same as the tabs: opening the toolbar puts the caret in it.
    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.IsOpen) && _viewModel.Search.IsOpen)
            Toolbar.Focus();
    }

    // Every appearance, not just the first: the edit form and the create page both pop
    // back here, and what they changed should show at once.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
