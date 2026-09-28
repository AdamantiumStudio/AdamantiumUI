using System;

namespace Adamantium.UI.Controls.Docking;

/// <summary>Raised before a pane leaves the layout; set <see cref="Cancel"/> to keep it where it was. Bulk closes ask per
/// pane, so one refusal stops only that pane.</summary>
public class PaneClosingEventArgs : EventArgs
{
    public PaneClosingEventArgs(string paneId, bool canRestore)
    {
        PaneId = paneId;
        CanRestore = canRestore;
    }

    public string PaneId { get; }

    /// <summary>Whether closing PUTS IT AWAY rather than destroys it - true for a tool, which comes back through
    /// <see cref="DockingArea.RestorePane"/>. A refusal matters far more when the answer here is false.</summary>
    public bool CanRestore { get; }

    /// <summary>Set true to refuse: this pane stays, and a bulk close carries on with the rest.</summary>
    public bool Cancel { get; set; }

    /// <summary>Set true to stop the WHOLE operation, not just this pane - what "Cancel" means in an editor's
    /// save-before-closing dialog. Without it, a bulk close would keep asking about every remaining tab after the user
    /// has already said stop. Implies <see cref="Cancel"/>.</summary>
    public bool CancelAll { get; set; }
}
