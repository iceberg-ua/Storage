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

    // Hardware back is a cancel, and cancelling has to clean up an uncommitted photo.
    protected override bool OnBackButtonPressed()
    {
        _ = _viewModel.CancelCommand.ExecuteAsync(null);
        return true;
    }
}
