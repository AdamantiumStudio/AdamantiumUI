namespace Adamantium.UI.Controls;

/// <summary>
/// How a <see cref="Popup"/> lines up with its target along the target's side: across for <see cref="PlacementMode.Top"/>
/// and <see cref="PlacementMode.Bottom"/>, down for <see cref="PlacementMode.Left"/> and <see cref="PlacementMode.Right"/>.
/// </summary>
public enum PlacementAlignment
{
    /// <summary>Centered on the target - a tooltip.</summary>
    Center,

    /// <summary>Left edges together, or top edges beside the target - a submenu level with its row.</summary>
    Start,

    /// <summary>Right edges together, or bottom edges beside the target.</summary>
    End
}
