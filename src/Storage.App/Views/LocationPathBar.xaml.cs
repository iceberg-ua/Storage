namespace Storage.App.Views;

/// <summary>
/// The path to the current location, shared by the Locations and Items tabs. Bind its
/// BindingContext to the <see cref="Services.LocationContext"/>; it hides itself at the
/// top level, where there is no path to show.
/// </summary>
public partial class LocationPathBar : ContentView
{
    public LocationPathBar()
    {
        InitializeComponent();
    }
}
