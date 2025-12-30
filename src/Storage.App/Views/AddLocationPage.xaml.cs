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
        await _viewModel.LoadParentLocationsCommand.ExecuteAsync(null);
    }
}
