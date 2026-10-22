using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Panels;

/// <summary>Tiles for every child of a <see cref="TiledPanel"/> no tile took, filled left to right and top to bottom, as
/// a photo grid or a video call lays them out: as many columns as gives the tiles the most room, at most
/// <see cref="MaxColumns"/> by <see cref="MaxRows"/>; the children past that are collapsed.</summary>
public class TileFlow : TileRegion
{
    public static readonly AdamantiumProperty MaxColumnsProperty = AdamantiumProperty.Register(nameof(MaxColumns),
        typeof(int), typeof(TileFlow), new PropertyMetadata(0));

    public static readonly AdamantiumProperty MaxRowsProperty = AdamantiumProperty.Register(nameof(MaxRows),
        typeof(int), typeof(TileFlow), new PropertyMetadata(0));

    public static readonly AdamantiumProperty AspectRatioProperty = AdamantiumProperty.Register(nameof(AspectRatio),
        typeof(double), typeof(TileFlow), new PropertyMetadata(0.0));

    public static readonly AdamantiumProperty LastRowAlignmentProperty = AdamantiumProperty.Register(
        nameof(LastRowAlignment), typeof(HorizontalAlignment), typeof(TileFlow),
        new PropertyMetadata(HorizontalAlignment.Left));

    /// <summary>The most columns; 0 sets no limit, 1 stacks the tiles in a column.</summary>
    public int MaxColumns
    {
        get => GetValue<int>(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <summary>The most rows; 0 sets no limit, 1 lines the tiles up in a row.</summary>
    public int MaxRows
    {
        get => GetValue<int>(MaxRowsProperty);
        set => SetValue(MaxRowsProperty, value);
    }

    /// <summary>The width of a tile over its height, kept with the tiles centered in the room; 0 stretches them over
    /// their cells.</summary>
    public double AspectRatio
    {
        get => GetValue<double>(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    /// <summary>Where a last row with fewer tiles stands; Stretch widens its tiles over the row.</summary>
    public HorizontalAlignment LastRowAlignment
    {
        get => GetValue<HorizontalAlignment>(LastRowAlignmentProperty);
        set => SetValue(LastRowAlignmentProperty, value);
    }
}
