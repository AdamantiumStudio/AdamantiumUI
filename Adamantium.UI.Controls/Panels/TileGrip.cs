using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Panels;

/// <summary>The cue a <see cref="TiledPanel"/> shows in each corner of the tile the pointer is over while tiles can be
/// split: a corner drags a new tile out of it. Drawn by the theme; it takes no input, the panel does.</summary>
public class TileGrip : Control
{
    public static readonly AdamantiumProperty CornerProperty = AdamantiumProperty.Register(nameof(Corner),
        typeof(TileCorner), typeof(TileGrip), new PropertyMetadata(TileCorner.TopLeft));

    public static readonly AdamantiumProperty IsHotProperty = AdamantiumProperty.Register(nameof(IsHot),
        typeof(bool), typeof(TileGrip), new PropertyMetadata(false));

    public TileGrip()
    {
        IsHitTestVisible = false;
    }

    /// <summary>The corner of the tile the grip stands in.</summary>
    public TileCorner Corner
    {
        get => GetValue<TileCorner>(CornerProperty);
        set => SetValue(CornerProperty, value);
    }

    /// <summary>Whether the pointer is on the grip, so a press there splits the tile.</summary>
    public bool IsHot
    {
        get => GetValue<bool>(IsHotProperty);
        set => SetValue(IsHotProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => null;
}
