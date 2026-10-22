using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Panels;

/// <summary>One tile of a <see cref="TiledPanel"/>'s layout, for one child: the child whose
/// <see cref="TiledPanel.TileProperty"/> is <see cref="Name"/>, or, when none is, the next child not placed yet.</summary>
public class Tile : TileRegion
{
    public static readonly AdamantiumProperty NameProperty = AdamantiumProperty.Register(nameof(Name),
        typeof(string), typeof(Tile), new PropertyMetadata(null));

    public Tile()
    {
    }

    public Tile(string name)
    {
        Name = name;
    }

    /// <summary>The name of the child the tile is for; null takes the children in their order.</summary>
    public string Name
    {
        get => GetValue<string>(NameProperty);
        set => SetValue(NameProperty, value);
    }
}
