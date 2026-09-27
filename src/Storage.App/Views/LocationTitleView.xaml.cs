namespace Storage.App.Views;

/// <summary>
/// The nav bar title for the Locations and Items tabs. Bind its BindingContext to the
/// <see cref="Services.LocationContext"/>: inside a location it shows a back arrow and
/// that location's name, at the top it shows <see cref="RootTitle"/>.
/// </summary>
public partial class LocationTitleView : ContentView
{
    public static readonly BindableProperty RootTitleProperty =
        BindableProperty.Create(nameof(RootTitle), typeof(string), typeof(LocationTitleView), string.Empty);

    public LocationTitleView()
    {
        InitializeComponent();
    }

    public string RootTitle
    {
        get => (string)GetValue(RootTitleProperty);
        set => SetValue(RootTitleProperty, value);
    }
}
