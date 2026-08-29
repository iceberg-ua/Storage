using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class ItemDetailPage : ContentPage
{
    private readonly ItemDetailViewModel _viewModel;

    public ItemDetailPage(ItemDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    // Fires again when the edit form pops back onto this page, which is exactly when
    // the values on show are stale — so this reloads every time, not just the first.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
