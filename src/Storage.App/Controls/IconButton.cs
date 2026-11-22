using System.Windows.Input;

namespace Storage.App.Controls;

/// <summary>
/// A button with one of the mockups' <see cref="Icons"/>, with or without a label beside
/// it: the round back, search and ⋯ buttons, "+ New tag", "Delete tag" and so on. A MAUI
/// Button only takes a bitmap image, which cannot follow the theme's colours, so this is
/// a Border that draws the icon itself. Shape, fill, padding and shadow come from a style
/// (RoundIconButton, StrongIconButton, …); TextColor colours the icon and the label alike.
/// </summary>
/// <remarks>
/// Give an icon-only one SemanticProperties.Description, as with any icon button: the
/// icon is not in the accessibility tree. A labelled one is announced by its label unless
/// it is given fuller words.
/// </remarks>
public class IconButton : Border
{
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph), typeof(string), typeof(IconButton), null, propertyChanged: (b, _, _) => ((IconButton)b).UpdateParts());

    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
        nameof(IconSize), typeof(double), typeof(IconButton), 20.0);

    public static readonly BindableProperty IconWeightProperty = BindableProperty.Create(
        nameof(IconWeight), typeof(double), typeof(IconButton), 2.0);

    public static readonly BindableProperty IconFilledProperty = BindableProperty.Create(
        nameof(IconFilled), typeof(bool), typeof(IconButton), false);

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(IconButton), null, propertyChanged: (b, _, _) => ((IconButton)b).UpdateParts());

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(IconButton), Colors.Black);

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(IconButton), 14.0);

    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
        nameof(FontFamily), typeof(string), typeof(IconButton), "JakartaBold");

    public static readonly BindableProperty SpacingProperty = BindableProperty.Create(
        nameof(Spacing), typeof(double), typeof(IconButton), 6.0);

    /// <summary>The icon after the label rather than before it: "Next ›".</summary>
    public static readonly BindableProperty IsIconAfterProperty = BindableProperty.Create(
        nameof(IsIconAfter), typeof(bool), typeof(IconButton), false, propertyChanged: (b, _, _) => ((IconButton)b).UpdateOrder());

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(IconButton), null, propertyChanged: OnCommandChanged);

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(IconButton), null, propertyChanged: (b, _, _) => ((IconButton)b).UpdateEnabled());

    private readonly Icon _icon = new();
    private readonly HorizontalStackLayout _row;
    private readonly Label _label = new() { VerticalOptions = LayoutOptions.Center, MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation };
    private bool _isPressing;

    // The command whose CanExecuteChanged this listens to, only while on screen: a view
    // model outlives its pages, and a list row's button held by its command would never
    // be freed. (Button does the same with a weak subscription.)
    private ICommand? _listening;

    // The description this set from Text, told apart from one the page gave.
    private string? _textDescription;

    /// <summary>Raised on a tap, after the command; for pages that handle taps in code.</summary>
    public event EventHandler? Clicked;

    public IconButton()
    {
        _icon.SetBinding(Icon.GlyphProperty, static (IconButton b) => b.Glyph, source: this);
        _icon.SetBinding(Icon.SizeProperty, static (IconButton b) => b.IconSize, source: this);
        _icon.SetBinding(Icon.WeightProperty, static (IconButton b) => b.IconWeight, source: this);
        _icon.SetBinding(Icon.IsFilledProperty, static (IconButton b) => b.IconFilled, source: this);
        _icon.SetBinding(Icon.ColorProperty, static (IconButton b) => b.TextColor, source: this);

        _label.SetBinding(Label.TextProperty, static (IconButton b) => b.Text, source: this);
        _label.SetBinding(Label.TextColorProperty, static (IconButton b) => b.TextColor, source: this);
        _label.SetBinding(Label.FontSizeProperty, static (IconButton b) => b.FontSize, source: this);
        _label.SetBinding(Label.FontFamilyProperty, static (IconButton b) => b.FontFamily, source: this);

        var row = _row = new HorizontalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { _icon, _label }
        };
        row.SetBinding(HorizontalStackLayout.SpacingProperty, static (IconButton b) => b.Spacing, source: this);
        Content = row;

        // The button is what a screen reader stops on, so the label is not a second stop.
        AutomationProperties.SetIsInAccessibleTree(_label, false);

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);

        Loaded += (_, _) => Listen();
        Unloaded += (_, _) => StopListening();

        UpdateParts();
    }

    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public double IconWeight { get => (double)GetValue(IconWeightProperty); set => SetValue(IconWeightProperty, value); }
    public bool IconFilled { get => (bool)GetValue(IconFilledProperty); set => SetValue(IconFilledProperty, value); }
    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Color TextColor { get => (Color)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public string FontFamily { get => (string)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public bool IsIconAfter { get => (bool)GetValue(IsIconAfterProperty); set => SetValue(IsIconAfterProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    private void UpdateParts()
    {
        _icon.IsVisible = !string.IsNullOrEmpty(Glyph);
        _label.IsVisible = !string.IsNullOrEmpty(Text);

        var description = SemanticProperties.GetDescription(this);
        if (description is null || description == _textDescription)
        {
            _textDescription = Text;
            SemanticProperties.SetDescription(this, Text);
        }
    }

    private void UpdateOrder()
    {
        _row.Children.Clear();
        if (IsIconAfter)
        {
            _row.Children.Add(_label);
            _row.Children.Add(_icon);
        }
        else
        {
            _row.Children.Add(_icon);
            _row.Children.Add(_label);
        }
    }

    private static void OnCommandChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var button = (IconButton)bindable;
        if (button.IsLoaded)
            button.Listen();
        else
            button.UpdateEnabled();
    }

    private void Listen()
    {
        StopListening();
        if (Command is { } command)
        {
            command.CanExecuteChanged += OnCanExecuteChanged;
            _listening = command;
        }

        UpdateEnabled();
    }

    private void StopListening()
    {
        if (_listening is { } command)
            command.CanExecuteChanged -= OnCanExecuteChanged;
        _listening = null;
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e) => UpdateEnabled();

    // Dimmed, as a disabled Button is in BaseButton.
    private void UpdateEnabled()
    {
        var enabled = Command?.CanExecute(CommandParameter) ?? true;
        IsEnabled = enabled;
        Opacity = enabled ? 1 : 0.4;
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled)
            return;

        if (Command is { } command && command.CanExecute(CommandParameter))
            command.Execute(CommandParameter);
        Clicked?.Invoke(this, EventArgs.Empty);

        // A short dip, so a tap is seen to land.
        if (_isPressing)
            return;
        _isPressing = true;
        try
        {
            await this.FadeToAsync(0.6, 60);
            await this.FadeToAsync(IsEnabled ? 1 : 0.4, 120);
        }
        finally
        {
            _isPressing = false;
        }
    }
}
