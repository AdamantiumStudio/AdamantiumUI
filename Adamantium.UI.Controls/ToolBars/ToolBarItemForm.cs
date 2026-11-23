namespace Adamantium.UI.Controls;

/// <summary>How a <see cref="ToolBarItem"/> shows its command, as its bar decides.</summary>
public enum ToolBarItemForm
{
    /// <summary>The command itself.</summary>
    Shown,

    /// <summary>A button in its place that opens the command on a card beside the column.</summary>
    Folded,

    /// <summary>Nothing: a command set not to stand on the column, or a label whose command shows it.</summary>
    Hidden
}
