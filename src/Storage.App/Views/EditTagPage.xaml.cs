using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class EditTagPage : ContentPage
{
    private readonly EditTagViewModel _viewModel;

    public EditTagPage(EditTagViewModel viewModel)
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
}
