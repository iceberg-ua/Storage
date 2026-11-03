namespace Storage.App.Views;

/// <summary>
/// A location's card in a list: name, a one-line description and its parent. Tapping
/// it runs the nearest <see cref="ViewModels.SearchViewModel"/>'s OpenLocation command.
/// Used by both location row templates, so they can't drift apart.
/// </summary>
public partial class LocationCard : ContentView
{
    public LocationCard()
    {
        InitializeComponent();
    }
}
