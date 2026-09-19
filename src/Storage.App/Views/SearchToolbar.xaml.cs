namespace Storage.App.Views;

/// <summary>
/// The search box and its scope controls, shared by every page that searches so the
/// two cannot drift apart again. Bind its BindingContext to a
/// <see cref="ViewModels.SearchViewModel"/>; it hides itself when that panel is closed.
/// </summary>
public partial class SearchToolbar : ContentView
{
    public SearchToolbar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Puts the caret in the box, so opening the toolbar does not cost a second tap
    /// before the keyboard appears.
    /// </summary>
    public new void Focus() => Input.Focus();
}
