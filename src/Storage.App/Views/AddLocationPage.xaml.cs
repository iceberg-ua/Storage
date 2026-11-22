using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class AddLocationPage : ContentPage
{
    private readonly AddLocationViewModel _viewModel;

    public AddLocationPage(AddLocationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    // Every way out of the page ends here, so this is where an unsaved photo is
    // reconciled. Ignored while the camera, the gallery picker or the place picker only
    // covers the page.
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await _viewModel.ReconcilePhotoOnLeaveAsync();
    }
}
