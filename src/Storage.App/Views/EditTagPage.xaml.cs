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

        // Page and view model live and die together, so there is nothing to unsubscribe.
        _viewModel.NameRequired += (_, _) => NameEntry.Focus();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
