namespace Adamantium.UI.Controls;

/// <summary>Where the commands stacked in one column of a ribbon group stand.</summary>
public enum RibbonRowAlignment
{
    /// <summary>From the top, <see cref="Panels.RibbonGroupPanel.RowSpacing"/> apart - as Word lays its rows out.</summary>
    Top,

    /// <summary>Spread over the group's height: the first at the top, the last at the bottom, even gaps between - never
    /// closer than <see cref="Panels.RibbonGroupPanel.RowSpacing"/>. A column of one command stays at the top; in a
    /// collapsed group's flyout the height is that of its tallest column.</summary>
    Spread
}
