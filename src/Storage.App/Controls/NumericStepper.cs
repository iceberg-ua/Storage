using System.Globalization;

namespace Storage.App.Controls;

/// <summary>
/// A numeric input with decrement/increment buttons flanking a directly editable entry.
/// MAUI's built-in <see cref="Stepper"/> has no inline text field, so the value can only be
/// nudged one step at a time — this control accepts keyboard entry as well.
/// </summary>
public class NumericStepper : ContentView
{
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(int),
        typeof(NumericStepper),
        1,
        BindingMode.TwoWay,
        propertyChanged: OnValueChanged,
        coerceValue: CoerceValue);

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum),
        typeof(int),
        typeof(NumericStepper),
        1,
        propertyChanged: OnLimitChanged);

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum),
        typeof(int),
        typeof(NumericStepper),
        int.MaxValue,
        propertyChanged: OnLimitChanged);

    private readonly Entry _entry;

    // Guards the Value <-> Entry.Text round trip from feeding back on itself.
    private bool _syncingText;

    public NumericStepper()
    {
        _entry = new Entry
        {
            Keyboard = Keyboard.Numeric,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            FontFamily = "JakartaExtraBold",
            FontSize = 18,
            // A floor, not a width: four digits and more still fit.
            MinimumWidthRequest = 56,
            Text = Value.ToString(CultureInfo.InvariantCulture)
        };
        SemanticProperties.SetDescription(_entry, "Quantity");
        _entry.TextChanged += OnEntryTextChanged;
        _entry.Unfocused += (_, _) => SyncEntryText();

        // Minus is the quiet one; plus, the common direction, carries the accent.
        var decrement = CreateStepButton("−", "Decrease quantity", () => Value--);
        decrement.SetAppThemeColor(Button.BackgroundColorProperty, Resource("SurfaceSunken"), Resource("SurfaceSunkenDark"));
        decrement.SetAppThemeColor(Button.TextColorProperty, Resource("Ink"), Resource("InkDark"));

        var increment = CreateStepButton("+", "Increase quantity", () => Value++);
        increment.SetAppThemeColor(Button.BackgroundColorProperty, Resource("Primary"), Resource("PrimaryDark"));
        increment.SetAppThemeColor(Button.TextColorProperty, Colors.White, Resource("PrimaryDarkText"));

        var row = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        row.Add(decrement);
        row.Add(_entry);
        row.Add(increment);

        // The same white box as the other fields beside it.
        var surface = new Border
        {
            StrokeThickness = 2,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(4, 0),
            HeightRequest = 52,
            Content = row
        };
        surface.SetAppThemeColor(BackgroundColorProperty, Resource("Surface"), Resource("SurfaceDark"));
        surface.SetAppThemeColor(Border.StrokeProperty, Resource("Hairline"), Resource("HairlineDark"));

        Content = surface;
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    private static Button CreateStepButton(string text, string description, Action onClicked)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 20,
            WidthRequest = 44,
            HeightRequest = 44,
            MinimumHeightRequest = 44,
            CornerRadius = 12,
            Padding = 0
        };
        SemanticProperties.SetDescription(button, description);

        button.Clicked += (_, _) => onClicked();
        return button;
    }

    // A colour from the app's palette; black only if the dictionary is missing, which
    // would already show everywhere else.
    private static Color Resource(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Black;

    private static object CoerceValue(BindableObject bindable, object value)
    {
        var stepper = (NumericStepper)bindable;
        return Math.Clamp((int)value, stepper.Minimum, stepper.Maximum);
    }

    private static void OnValueChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((NumericStepper)bindable).SyncEntryText();
    }

    private static void OnLimitChanged(BindableObject bindable, object oldValue, object newValue)
    {
        // Pull the current value back inside the new bounds.
        var stepper = (NumericStepper)bindable;
        var clamped = Math.Clamp(stepper.Value, stepper.Minimum, stepper.Maximum);
        if (clamped != stepper.Value)
            stepper.Value = clamped;
    }

    private void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingText)
            return;

        // An empty field is a normal intermediate state while typing; it gets
        // normalised back to a valid number when the entry loses focus.
        if (string.IsNullOrEmpty(e.NewTextValue))
            return;

        if (int.TryParse(e.NewTextValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            Value = parsed;
        else
            SyncEntryText();
    }

    private void SyncEntryText()
    {
        var text = Value.ToString(CultureInfo.InvariantCulture);
        if (_entry.Text == text)
            return;

        _syncingText = true;
        _entry.Text = text;
        _syncingText = false;
    }
}
