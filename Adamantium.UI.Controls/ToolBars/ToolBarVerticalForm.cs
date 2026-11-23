namespace Adamantium.UI.Controls;

/// <summary>How a command of a <see cref="ToolBar"/> stands on the bar's column when the bar runs down the side; set on
/// the command. Along a bar, and in its overflow menu, every command stands as it is.</summary>
public enum ToolBarVerticalForm
{
    /// <summary>As it is while it fits the column - in its own narrow form, if its theme draws one - and folded into a
    /// button when it does not.</summary>
    Auto,

    /// <summary>Always folded into a button that opens the command beside the column.</summary>
    Fold,

    /// <summary>As it is, however wide: the column grows to it.</summary>
    Keep,

    /// <summary>Not on the column.</summary>
    Hide
}
