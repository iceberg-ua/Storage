using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class CreatePage : ContentPage
{
    private readonly CreateViewModel _viewModel;

    public CreatePage(CreateViewModel viewModel)
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

    // The same arrangement as the item edit page: every way out ends here, so this is
    // where photos staged on the item form are reconciled. It is ignored while the page
    // is only covered by the camera or the gallery picker.
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await _viewModel.LeaveAsync();
    }
}
