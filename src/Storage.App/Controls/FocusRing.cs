namespace Storage.App.Controls;

/// <summary>
/// Set on an Entry or Editor inside a FieldSurface border: while the field has focus, the
/// border's stroke takes the accent colour. Only the colour: FieldSurface's stroke is
/// already the ring's width, so the field does not shift. The border goes back to its style after.
/// </summary>
/// <remarks>
/// An attached property on the field rather than a trigger on the border: a trigger would
/// bind to the field by x:Reference, which compiled bindings in a typed page do not resolve.
/// </remarks>
public static class FocusRing
{
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled",
        typeof(bool),
        typeof(FocusRing),
        false,
        propertyChanged: OnIsEnabledChanged);

    public static bool GetIsEnabled(BindableObject view) => (bool)view.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(BindableObject view, bool value) => view.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not InputView field)
            return;

        field.Focused -= OnFocused;
        field.Unfocused -= OnUnfocused;

        if (newValue is true)
        {
            field.Focused += OnFocused;
            field.Unfocused += OnUnfocused;
        }
    }

    private static void OnFocused(object? sender, FocusEventArgs e)
    {
        if (FindSurface(sender) is not { } surface)
            return;

        // Per theme, so switching light and dark while the field has focus follows along.
        if (Resource("Primary") is { } light && Resource("PrimaryDark") is { } dark)
            surface.SetAppThemeColor(Border.StrokeProperty, light, dark);
    }

    private static Color? Resource(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as Color : null;

    private static void OnUnfocused(object? sender, FocusEventArgs e)
    {
        if (FindSurface(sender) is not { } surface)
            return;

        // Back to whatever the FieldSurface style says, theme binding included.
        surface.ClearValue(Border.StrokeProperty);
    }

    // The nearest border up the tree: the field may sit in a layout inside it.
    private static Border? FindSurface(object? sender)
    {
        for (var element = (sender as Element)?.Parent; element is not null; element = element.Parent)
        {
            if (element is Border border)
                return border;
        }

        return null;
    }
}
