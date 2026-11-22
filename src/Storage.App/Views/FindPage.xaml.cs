using Storage.App.ViewModels;

namespace Storage.App.Views;

/// <summary>
/// The Find tab, and the same page pushed as "search" from a location's page or the
/// Locations tab, with the place or the Places tab already chosen.
/// </summary>
public partial class FindPage : ContentPage
{
    private readonly FindViewModel _viewModel;

    public FindPage(FindViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    // Every appearance, so an item edited or added elsewhere shows as it is now. LoadAsync
    // catches its own failures, since an exception escaping this async void handler would
    // end the app.
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Asked for by Home's search box. Here, once the page is on screen, since a box
        // that is not attached yet ignores Focus.
        if (_viewModel.TakeFocusRequest())
            Input.Focus();

        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
