using System;
using System.Collections.Generic;

namespace Adamantium.UI.Controls.Docking;

/// <summary>Raised before panes leave the layout for a window of their own; set <see cref="Cancel"/> to keep them docked.
/// Separate from docking, since a pane may move freely yet not float.</summary>
public class PaneTearingOffEventArgs : EventArgs
{
    public PaneTearingOffEventArgs(IReadOnlyList<string> panes, bool isWholePanel)
    {
        Panes = panes;
        IsWholePanel = isWholePanel;
    }

    /// <summary>Every pane that would leave - one for a dragged tab, all of them for a panel dragged by its caption.</summary>
    public IReadOnlyList<string> Panes { get; }

    /// <summary>Whether the whole PANEL is going (dragged by its caption) rather than a single tab.</summary>
    public bool IsWholePanel { get; }

    /// <summary>Set true to refuse: nothing leaves, and no window is opened.</summary>
    public bool Cancel { get; set; }
}
