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

        // The header sits inside the list, so it is pointed at the page's view model
        // directly rather than found by a RelativeSource lookup from inside the header,
        // which is unreliable on Android.
        Header.BindingContext = _viewModel;
    }

    /// <summary>Which location this page shows, for jumping back to it from a deeper one.</summary>
    public int LocationId => _viewModel.LocationId;

    // Every appearance, not just the first: the edit form and the create page both pop
    // back here, and what they changed should show at once. LoadAsync catches its own
    // failures, since an exception escaping this async void handler would end the app.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
