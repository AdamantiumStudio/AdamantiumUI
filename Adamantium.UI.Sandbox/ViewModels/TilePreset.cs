using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Panels;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>One of <see cref="TileLayouts"/> as the tiled panel stand offers it: a thumbnail of its own, with a tile in
/// each place, and a fresh tree for the panel when picked.</summary>
public sealed class TilePreset
{
    private readonly Func<TileRegion> _make;

    public TilePreset(string name, Func<TileRegion> make, IReadOnlyList<string> palette)
    {
        _make = make;
        Name = name;
        Thumbnail = make();
        var tiles = new List<ColorRect>();
        for (var i = 0; i < CountTiles(Thumbnail); i++)
        {
            tiles.Add(new ColorRect { Number = i + 1, Color = palette[i % palette.Count] });
        }

        Tiles = tiles;
    }

    /// <summary>The layout's name in <see cref="TileLayouts"/>, the key of its phrase.</summary>
    public string Name { get; }

    /// <summary>The thumbnail's own tree, so a drag on the panel never reaches it.</summary>
    public TileRegion Thumbnail { get; }

    /// <summary>A tile for each place of the layout.</summary>
    public IReadOnlyList<ColorRect> Tiles { get; }

    /// <summary>A new tree of the layout.</summary>
    public TileRegion Make() => _make();

    private static int CountTiles(TileRegion region) => region switch
    {
        TileSplit split => SumTiles(split),
        _ => 1
    };

    private static int SumTiles(TileSplit split)
    {
        var count = 0;
        foreach (var part in split.Children)
        {
            count += CountTiles(part);
        }

        return count;
    }
}
