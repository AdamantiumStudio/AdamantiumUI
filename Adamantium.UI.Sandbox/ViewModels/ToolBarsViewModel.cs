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

    /// <summary>Where the sections that do not fit on the bar of sections go: to its menu, or onto a new line.</summary>
    [Bindable] private Adamantium.UI.Controls.CommandWrapping _sectionsWrapping;

    /// <summary>Which way the picture's tray runs: down the side, its commands in their narrow forms or folded; or along.</summary>
    [Bindable] private Adamantium.UI.Controls.Panels.Orientation _sideOrientation = Adamantium.UI.Controls.Panels.Orientation.Vertical;

    /// <summary>The picture's size, in centimetres, as its two number boxes show it.</summary>
    [Bindable] private double? _pictureWidth = 5.08;

    [Bindable] private double? _pictureHeight = 3.49;

    /// <summary>Who wrote the picture's caption: a field too wide for the column, so folded down the side.</summary>
    [Bindable] private string _author = "Ann";

    /// <summary>The caption's font, picked from a list too wide for the column.</summary>
    [Bindable] private string _font = "Inter";

    /// <summary>How the picture stands among the text: the keys of the wrap button's list, among the page's phrases.</summary>
    public string[] WrapKinds { get; } = ["InLine", "Square", "InFront"];

    /// <summary>The ways the rotate button's list turns the picture.</summary>
    public string[] RotateKinds { get; } = ["RotateRight", "RotateLeft"];

    public string[] Fonts { get; } = ["Inter", "Georgia", "Consolas"];

    public ToolBarsViewModel() : base("ToolBars")
    {
    }

    [Command] private void Press(object command) => Pressed = command as string ?? "Nothing";
}
