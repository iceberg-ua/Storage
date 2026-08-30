using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class AddItemPage : ContentPage
{
    private readonly AddItemViewModel _viewModel;

    public AddItemPage(AddItemViewModel viewModel)
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

    // Every way out of this page ends here — Cancel, hardware back, the gesture, and
    // Shell's own back arrow — so this is the one place staged photos have to be
    // reconciled. The ViewModel ignores the call when the page is merely being covered
    // by the camera page or the system gallery picker.
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await _viewModel.ReconcilePhotosOnLeaveAsync();
    }
}
