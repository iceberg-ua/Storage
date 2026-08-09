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
            Text = Value.ToString(CultureInfo.InvariantCulture)
        };
        _entry.TextChanged += OnEntryTextChanged;
        _entry.Unfocused += (_, _) => SyncEntryText();

        var decrement = CreateStepButton("−", () => Value--);
        var increment = CreateStepButton("+", () => Value++);

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };

        grid.Add(decrement, 0);
        grid.Add(_entry, 1);
        grid.Add(increment, 2);

        Content = grid;
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

    private static Button CreateStepButton(string text, Action onClicked)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 20,
            WidthRequest = 52,
            HeightRequest = 44,
            Padding = 0
        };

        button.Clicked += (_, _) => onClicked();
        return button;
    }

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
