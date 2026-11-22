using System.Windows.Input;
using Storage.App.Controls;

namespace Storage.App.Views;

/// <summary>
/// The tab bar drawn by the tab pages themselves; see AppTabBar.xaml. A tab switches with
/// a Shell route ("//tags"), so Shell still keeps each tab's page and its stack.
/// </summary>
public partial class AppTabBar : ContentView
{
    /// <summary>The route of the tab this bar sits on: home, locations, tags or find.</summary>
    public static readonly BindableProperty CurrentProperty = BindableProperty.Create(
        nameof(Current), typeof(string), typeof(AppTabBar), "home", propertyChanged: (b, _, _) => ((AppTabBar)b).UpdateTabs());

    /// <summary>What the camera button does: the page's New item, which may start in a place.</summary>
    public static readonly BindableProperty SnapCommandProperty = BindableProperty.Create(
        nameof(SnapCommand), typeof(ICommand), typeof(AppTabBar), null,
        propertyChanged: (b, _, value) => ((AppTabBar)b).SnapButton.Command = value as ICommand);

    private bool _isSwitching;

    public AppTabBar()
    {
        InitializeComponent();
        UpdateTabs();
    }

    public string Current
    {
        get => (string)GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    public ICommand? SnapCommand
    {
        get => (ICommand?)GetValue(SnapCommandProperty);
        set => SetValue(SnapCommandProperty, value);
    }

    private async void OnTabTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string route || route == Current || _isSwitching)
            return;

        _isSwitching = true;
        try
        {
            await Shell.Current.GoToAsync($"//{route}");
        }
        finally
        {
            _isSwitching = false;
        }
    }

    // The selected tab in the accent, bolder and with a heavier line; the rest muted.
    // Colours are set per theme, as an AppThemeBinding would, so they follow a theme
    // change without this listening to the app (which would keep every bar alive).
    private void UpdateTabs()
    {
        var accent = (Light: Resource("Primary"), Dark: Resource("PrimaryDark"));
        var muted = (Light: Resource("InkMuted"), Dark: Resource("InkMutedDark"));

        Style("home", HomeTab, HomeIcon, HomeLabel);
        Style("locations", LocationsTab, LocationsIcon, LocationsLabel);
        Style("tags", TagsTab, TagsIcon, TagsLabel);
        Style("find", FindTab, FindIcon, FindLabel);

        void Style(string route, View tab, Icon icon, Label label)
        {
            var selected = route == Current;
            var colour = selected ? accent : muted;
            icon.SetAppThemeColor(Icon.ColorProperty, colour.Light, colour.Dark);
            icon.Weight = selected ? 2 : 1.8;
            label.SetAppThemeColor(Label.TextColorProperty, colour.Light, colour.Dark);
            label.FontFamily = selected ? "JakartaBold" : "JakartaSemiBold";
            SemanticProperties.SetDescription(tab, selected ? $"{label.Text}, selected" : label.Text);
        }
    }

    private static Color Resource(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Gray;
}
