namespace Storage.App.Views;

/// <summary>A location card's picture. Bind it to a <see cref="ViewModels.PlaceTile"/>.</summary>
public partial class PlaceCover : ContentView
{
    /// <summary>The gap between the photos of the mosaic: 4 on a card, 3 on a row's small cover.</summary>
    public static readonly BindableProperty GapProperty = BindableProperty.Create(
        nameof(Gap), typeof(double), typeof(PlaceCover), 4.0,
        propertyChanged: (b, _, value) =>
        {
            var cover = (PlaceCover)b;
            cover.Mosaic.RowSpacing = (double)value;
            cover.Mosaic.ColumnSpacing = (double)value;
        });

    public PlaceCover()
    {
        InitializeComponent();
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }
}
