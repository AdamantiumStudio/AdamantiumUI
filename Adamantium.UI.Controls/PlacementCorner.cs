namespace Adamantium.UI.Controls;

/// <summary>
/// The corner of its target a <see cref="Popup"/> placed <see cref="PlacementMode.Relative"/> is offset from: the popup's
/// same corner stands that far in from it.
/// </summary>
public enum PlacementCorner
{
    /// <summary>The offset is from the target's top-left corner - the usual one.</summary>
    TopLeft,

    /// <summary>From its top-right corner: the horizontal offset is the room to the target's right edge.</summary>
    TopRight,

    /// <summary>From its bottom-left corner: the vertical offset is the room to the target's bottom edge.</summary>
    BottomLeft,

    /// <summary>From its bottom-right corner: both offsets are the room to the target's edges.</summary>
    BottomRight
}
