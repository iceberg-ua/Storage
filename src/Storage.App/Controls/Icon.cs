using Microsoft.Maui.Controls.Shapes;
using MauiPath = Microsoft.Maui.Controls.Shapes.Path;

namespace Storage.App.Controls;

/// <summary>
/// One of <see cref="Icons"/>, drawn as a vector at <see cref="Size"/> in
/// <see cref="Color"/>: the mockups' line icons, in place of emoji, which look different on
/// every phone and cannot take the theme's colours.
/// </summary>
/// <remarks>
/// The glyphs sit on a 24-unit grid, so the geometry is scaled by Size / 24 rather than
/// stretched to fit: stretching would blow a small glyph such as a chevron up to the full
/// box. The stroke is not scaled with it, so <see cref="Weight"/> is converted here.
/// Decorative: hidden from screen readers, so the control it sits in carries the words.
/// </remarks>
public class Icon : ContentView
{
    private readonly MauiPath _path = new()
    {
        Aspect = Stretch.None,
        StrokeLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        HorizontalOptions = LayoutOptions.Start,
        VerticalOptions = LayoutOptions.Start
    };

    private static readonly PathGeometryConverter Converter = new();

    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph), typeof(string), typeof(Icon), null, propertyChanged: (b, _, _) => ((Icon)b).UpdateGeometry());

    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size), typeof(double), typeof(Icon), 20.0, propertyChanged: (b, _, _) => ((Icon)b).UpdateScale());

    /// <summary>The stroke width on the 24-unit grid, as the SVG's stroke-width.</summary>
    public static readonly BindableProperty WeightProperty = BindableProperty.Create(
        nameof(Weight), typeof(double), typeof(Icon), 2.0, propertyChanged: (b, _, _) => ((Icon)b).UpdateScale());

    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color), typeof(Color), typeof(Icon), Colors.Black, propertyChanged: (b, _, _) => ((Icon)b).UpdatePaint());

    /// <summary>Filled instead of outlined: the ⋯ dots, the main photo's heart.</summary>
    public static readonly BindableProperty IsFilledProperty = BindableProperty.Create(
        nameof(IsFilled), typeof(bool), typeof(Icon), false, propertyChanged: (b, _, _) => ((Icon)b).UpdatePaint());

    public Icon()
    {
        Content = _path;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(this, false);
        UpdateScale();
        UpdatePaint();
    }

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double Weight
    {
        get => (double)GetValue(WeightProperty);
        set => SetValue(WeightProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public bool IsFilled
    {
        get => (bool)GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    private void UpdateGeometry() =>
        _path.Data = string.IsNullOrEmpty(Glyph) ? null : (Geometry?)Converter.ConvertFromInvariantString(Glyph);

    private void UpdateScale()
    {
        var scale = Size / 24;
        WidthRequest = Size;
        HeightRequest = Size;
        _path.WidthRequest = Size;
        _path.HeightRequest = Size;
        _path.RenderTransform = new ScaleTransform(scale, scale);
        _path.StrokeThickness = IsFilled ? 0 : Weight * scale;
    }

    private void UpdatePaint()
    {
        var brush = new SolidColorBrush(Color);
        _path.Stroke = brush;
        _path.Fill = IsFilled ? brush : null;
        _path.StrokeThickness = IsFilled ? 0 : Weight * Size / 24;
    }
}
