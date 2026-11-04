using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class ItemForm : ContentView
{
    // The form is handed its view model through binding inheritance, so it can't take
    // it in the constructor; it follows BindingContext instead.
    private AddItemViewModel? _viewModel;

    public ItemForm()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_viewModel is not null)
            _viewModel.NameRequired -= OnNameRequired;

        _viewModel = BindingContext as AddItemViewModel;

        if (_viewModel is not null)
            _viewModel.NameRequired += OnNameRequired;
    }

    private void OnNameRequired(object? sender, EventArgs e) => NameEntry.Focus();
}
