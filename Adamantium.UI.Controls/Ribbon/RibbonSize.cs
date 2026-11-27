namespace Adamantium.UI.Controls;

/// <summary>How much room a ribbon command takes in its group. The author states the largest
/// (<see cref="Ribbon.MaxSizeProperty"/>) and the steps of its group at which it shrinks
/// (<see cref="Ribbon.CollapseToMediumProperty"/>, <see cref="Ribbon.CollapseToSmallProperty"/>); the group decides the
/// actual <see cref="Ribbon.SizeProperty"/>.</summary>
public enum RibbonSize
{
    /// <summary>Big icon over the label - one per column.</summary>
    Large,

    /// <summary>Small icon with the label beside it - three per column.</summary>
    Medium,

    /// <summary>Icon only - three per column.</summary>
    Small
}
