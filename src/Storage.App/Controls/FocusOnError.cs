namespace Storage.App.Controls;

/// <summary>
/// Attached to an Entry and bound to its field's error text: when an error appears, the
/// field is scrolled to the top of its ScrollView and focused.
/// </summary>
/// <remarks>
/// An attached property rather than a Behavior: it is set on the Entry itself, so it binds
/// against the Entry's BindingContext with nothing to forward.
/// </remarks>
public static class FocusOnError
{
    public static readonly BindableProperty MessageProperty = BindableProperty.CreateAttached(
        "Message",
        typeof(string),
        typeof(FocusOnError),
        null,
        propertyChanged: OnMessageChanged);

    public static string? GetMessage(BindableObject view) => (string?)view.GetValue(MessageProperty);

    public static void SetMessage(BindableObject view, string? value) => view.SetValue(MessageProperty, value);

    private static async void OnMessageChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not Entry entry || newValue is not string { Length: > 0 })
            return;

        // Android pans the window only far enough to show the focused Entry, which can
        // leave the message under it behind the keyboard. Starting the field's container
        // (the entry plus its message) at the top of the scroll area keeps both clear.
        if (FindScrollView(entry) is { } scrollView)
        {
            try
            {
                await scrollView.ScrollToAsync(entry.Parent as Element ?? entry, ScrollToPosition.Start, animated: true);
            }
            catch (Exception)
            {
                // Scrolling is a nicety; an async void handler must not let it crash the app.
            }
        }

        entry.Focus();
    }

    private static ScrollView? FindScrollView(Element element)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ScrollView scrollView)
                return scrollView;
        }

        return null;
    }
}
