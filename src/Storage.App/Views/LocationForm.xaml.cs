using Storage.App.ViewModels;

namespace Storage.App.Views;

public partial class LocationForm : ContentView
{
    // The form is handed its view model through binding inheritance, so it can't take
    // it in the constructor; it follows BindingContext instead.
    private AddLocationViewModel? _viewModel;

    public LocationForm()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_viewModel is not null)
            _viewModel.NameRequired -= OnNameRequired;

        _viewModel = BindingContext as AddLocationViewModel;

        if (_viewModel is not null)
            _viewModel.NameRequired += OnNameRequired;
    }

    private void OnNameRequired(object? sender, EventArgs e) => NameEntry.Focus();
}
