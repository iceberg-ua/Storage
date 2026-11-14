using System.ComponentModel;
using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class LocationsPage : ContentPage
{
    private readonly LocationsViewModel _viewModel;

    public LocationsPage(LocationsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        _viewModel.Search.PropertyChanged += OnSearchPropertyChanged;
    }

    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.IsOpen) && _viewModel.Search.IsOpen)
            Toolbar.Focus();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadLocationsCommand.ExecuteAsync(null);
    }
}
