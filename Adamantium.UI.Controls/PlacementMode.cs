namespace Adamantium.UI.Controls;

/// <summary>
/// Where a <see cref="Popup"/> positions its child relative to its <see cref="Popup.PlacementTarget"/>. The popup
/// re-evaluates this every frame, so it follows a moving target (e.g. a tooltip riding a slider thumb).
/// </summary>
public enum PlacementMode
{
    /// <summary>Below the target, lined up by <see cref="Popup.PlacementAlignment"/>.</summary>
    Bottom,

    /// <summary>Above the target, lined up by <see cref="Popup.PlacementAlignment"/>.</summary>
    Top,

    /// <summary>To the left of the target, lined up by <see cref="Popup.PlacementAlignment"/>.</summary>
    Left,

    /// <summary>To the right of the target, lined up by <see cref="Popup.PlacementAlignment"/>.</summary>
    Right,

    /// <summary>Centered over the target.</summary>
    Center,

    /// <summary>At the target's top-left corner (then offset) - the "exact position" mode.</summary>
    Relative
}
