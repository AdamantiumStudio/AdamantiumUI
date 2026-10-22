using System;
using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Panels;

/// <summary>A part of a <see cref="TiledPanel"/>'s layout: one <see cref="Tile"/>, a <see cref="TileFlow"/> of tiles, or
/// a <see cref="TileSplit"/> into more parts. A layout is a tree of them, plain data built in markup or code;
/// <see cref="TileLayouts"/> has the usual ones.</summary>
public abstract class TileRegion : AdamantiumComponent
{
    /// <summary>The region, or a region inside it, changed how it is laid out.</summary>
    public event EventHandler Changed;

    /// <summary>Gives every part of every split inside the region an even share of its room again.</summary>
    public virtual void ShareEvenly()
    {
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        RaiseChanged();
    }

    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
