using Adamantium.Core.Collections;

namespace Adamantium.UI.Controls.Panels;

/// <summary>The usual <see cref="TiledPanel"/> layouts: the viewport layouts of 3D editors, a gallery, and one featured
/// tile with the rest in a strip beside it. Every read makes a new tree, so the panels using one never share a drag.
/// The tiles are taken in reading order: left to right, top to bottom.</summary>
public static class TileLayouts
{
    /// <summary>The name of the large tile of the featured layouts.</summary>
    public const string FeaturedTile = "Featured";

    /// <summary>One tile over the whole panel, in a split of one part so that it can be split further.</summary>
    public static TileRegion One => Split(Orientation.Horizontal, new Tile());

    /// <summary>Two tiles side by side.</summary>
    public static TileRegion TwoColumns => Split(Orientation.Horizontal, new Tile(), new Tile());

    /// <summary>Two tiles one above the other.</summary>
    public static TileRegion TwoRows => Split(Orientation.Vertical, new Tile(), new Tile());

    /// <summary>A large tile on the left, two stacked on the right of it.</summary>
    public static TileRegion ThreeLeft => Split(Orientation.Horizontal, new Tile(), Stack(2));

    /// <summary>Two tiles stacked on the left, a large one on the right of them.</summary>
    public static TileRegion ThreeRight => Split(Orientation.Horizontal, Stack(2), new Tile());

    /// <summary>A large tile on top, two side by side under it.</summary>
    public static TileRegion ThreeTop => Split(Orientation.Vertical, new Tile(), Row(2));

    /// <summary>Two tiles side by side on top, a large one under them.</summary>
    public static TileRegion ThreeBottom => Split(Orientation.Vertical, Row(2), new Tile());

    /// <summary>Four tiles, two by two.</summary>
    public static TileRegion Four => Split(Orientation.Vertical, Row(2), Row(2));

    /// <summary>A large tile on the left, three stacked on the right of it.</summary>
    public static TileRegion FourLeft => Split(Orientation.Horizontal, 2, new Tile(), Stack(3));

    /// <summary>Three tiles stacked on the left, a large one on the right of them.</summary>
    public static TileRegion FourRight => Split(Orientation.Horizontal, 0.5, Stack(3), new Tile());

    /// <summary>A large tile on top, three side by side under it.</summary>
    public static TileRegion FourTop => Split(Orientation.Vertical, 2, new Tile(), Row(3));

    /// <summary>Three tiles side by side on top, a large one under them.</summary>
    public static TileRegion FourBottom => Split(Orientation.Vertical, 0.5, Row(3), new Tile());

    /// <summary>Every child in a <see cref="TileFlow"/>.</summary>
    public static TileRegion Gallery => new TileFlow();

    /// <summary>The <see cref="FeaturedTile"/> large, the rest in a row under it.</summary>
    public static TileRegion FeaturedBottom =>
        Split(Orientation.Vertical, 4, new Tile(FeaturedTile), new TileFlow { MaxRows = 1 });

    /// <summary>The <see cref="FeaturedTile"/> large, the rest in a row over it.</summary>
    public static TileRegion FeaturedTop =>
        Split(Orientation.Vertical, 0.25, new TileFlow { MaxRows = 1 }, new Tile(FeaturedTile));

    /// <summary>The <see cref="FeaturedTile"/> large, the rest in a column on its left.</summary>
    public static TileRegion FeaturedLeft =>
        Split(Orientation.Horizontal, 0.25, new TileFlow { MaxColumns = 1 }, new Tile(FeaturedTile));

    /// <summary>The <see cref="FeaturedTile"/> large, the rest in a column on its right.</summary>
    public static TileRegion FeaturedRight =>
        Split(Orientation.Horizontal, 4, new Tile(FeaturedTile), new TileFlow { MaxColumns = 1 });

    private static TileSplit Split(Orientation orientation, params TileRegion[] parts) => new(orientation, parts);

    private static TileSplit Split(Orientation orientation, double firstToSecond, TileRegion first, TileRegion second) =>
        new(orientation, first, second) { Shares = [firstToSecond, 1] };

    private static TileSplit Stack(int count) => Split(Orientation.Vertical, Tiles(count));

    private static TileSplit Row(int count) => Split(Orientation.Horizontal, Tiles(count));

    private static TileRegion[] Tiles(int count)
    {
        var tiles = new TileRegion[count];
        for (var i = 0; i < count; i++)
        {
            tiles[i] = new Tile();
        }

        return tiles;
    }
}
