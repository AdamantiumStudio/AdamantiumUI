namespace Adamantium.UI.Controls.Panels;

/// <summary>The scheme a <see cref="TiledPanel"/> lays its children out in, as 3D editors lay out their viewports; the
/// children take the tiles in their order.</summary>
public enum TileLayout
{
    /// <summary>One tile over the whole panel.</summary>
    One,

    /// <summary>Two tiles side by side.</summary>
    TwoColumns,

    /// <summary>Two tiles one above the other.</summary>
    TwoRows,

    /// <summary>A large tile on the left, two stacked on the right of it.</summary>
    ThreeLeft,

    /// <summary>A large tile on top, two side by side under it.</summary>
    ThreeTop,

    /// <summary>Four tiles, two by two: top left, top right, bottom left, bottom right.</summary>
    Four,

    /// <summary>A large tile on the left, three stacked on the right of it.</summary>
    FourLeft
}
