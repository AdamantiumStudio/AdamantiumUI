using System;
using Adamantium.MVVM;
using Adamantium.UI.Controls.Panels;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A tile of the tiled panel stand's gallery or featured layout - a photo, a caller - that can be featured.</summary>
[ViewModel]
public partial class ShowcaseTile
{
    private readonly Action<ShowcaseTile> _feature;

    public ShowcaseTile(int number, string color, Action<ShowcaseTile> feature)
    {
        Number = number;
        Color = color;
        _feature = feature;
    }

    public int Number { get; }

    public string Color { get; }

    /// <summary>Whether the tile is the featured one.</summary>
    [Bindable, Affects(nameof(TileName), nameof(CanMove))]
    private bool _isFeatured;

    /// <summary>The tile of the layout it takes: the featured one, or the next in order.</summary>
    public string TileName => IsFeatured ? TileLayouts.FeaturedTile : null;

    /// <summary>Whether the tile drags to another place: the featured one has its own tile.</summary>
    public bool CanMove => !IsFeatured;

    /// <summary>Makes this tile the featured one.</summary>
    [Command]
    private void Feature() => _feature(this);
}
