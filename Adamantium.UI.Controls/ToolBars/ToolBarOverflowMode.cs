namespace Adamantium.UI.Controls;

/// <summary>Where a command of a <see cref="ToolBar"/> stands when the bar is short of room.</summary>
public enum ToolBarOverflowMode
{
    /// <summary>On the bar while there is room; in the bar's overflow menu when there is not, the last commands first.</summary>
    AsNeeded,

    /// <summary>Always in the overflow menu.</summary>
    Always,

    /// <summary>Always on the bar.</summary>
    Never
}
