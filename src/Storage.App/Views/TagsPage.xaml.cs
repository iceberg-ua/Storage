using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class TagsPage : ContentPage
{
    private readonly TagsViewModel _viewModel;

    public TagsPage(TagsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    // Reloads on every appearance so a rename, recolour, or new tag made on the edit
    // page is reflected when we come back to this one.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadTagsCommand.ExecuteAsync(null);
    }
}
