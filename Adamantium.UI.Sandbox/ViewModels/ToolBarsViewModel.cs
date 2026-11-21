using Adamantium.MVVM;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Toolbars tab: two trays of bars that move by their grips, shown and hidden from the tray's menu; a press on a
/// command says which it was.</summary>
[ViewModel]
public partial class ToolBarsViewModel : TabPageViewModel
{
    /// <summary>The key of the command pressed last, among the page's phrases; none before the first.</summary>
    [Bindable] private string _pressed = "Nothing";

    /// <summary>How wide the upper tray may be: narrowed, its bars send their last commands to their menus.</summary>
    [Bindable] private double _trayWidth = 900;

    /// <summary>How wide the tray of sections may be: narrowed, its bar sends its last sections to its menu, whole.</summary>
    [Bindable] private double _sectionsWidth = 900;

    /// <summary>Whether the user stands in a table, as the caret would: the table's contextual bar shows while so.</summary>
    [Bindable] private bool _inTable;

    public ToolBarsViewModel() : base("ToolBars")
    {
    }

    [Command] private void Press(object command) => Pressed = command as string ?? "Nothing";
}
