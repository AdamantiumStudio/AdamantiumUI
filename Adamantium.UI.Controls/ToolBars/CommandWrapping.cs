namespace Adamantium.UI.Controls;

/// <summary>Where the commands of a <see cref="ToolBar"/> that do not fit on its line go.</summary>
public enum CommandWrapping
{
    /// <summary>To the bar's overflow menu, the last first.</summary>
    NoWrap,

    /// <summary>Onto a new line of the bar, each whole - a section never split between two lines; the bar grows across.
    /// A command set to stand always in the menu still goes there.</summary>
    Wrap
}
