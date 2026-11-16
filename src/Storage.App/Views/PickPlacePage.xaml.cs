using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class PickPlacePage : ContentPage
{
    private readonly PickPlaceViewModel _viewModel;

    public PickPlacePage(PickPlaceViewModel viewModel)
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

    // However the page goes — confirm, close, or Android's Back — the caller's await
    // must end. After a confirm this is a no-op: the pick was completed first.
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Abandon();
    }
}
