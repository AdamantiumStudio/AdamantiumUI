using System;
using System.Collections.Generic;

namespace Adamantium.UI.Controls.Docking;

/// <summary>Raised once on the drop, before panes are docked; set <see cref="Cancel"/> to refuse. For rules
/// <see cref="Pane.Allowed"/> zones cannot express.</summary>
public class PaneDockingEventArgs : EventArgs
{
    public PaneDockingEventArgs(IReadOnlyList<string> panes, PaneNode target, DockZone zone)
    {
        Panes = panes;
        Target = target;
        Zone = zone;
    }

    /// <summary>Every pane that would move - one for a dragged tab, all of them for a panel or a floating window.</summary>
    public IReadOnlyList<string> Panes { get; }

    /// <summary>What it would be docked against: a group to be tabbed into, or the root for an edge anchor.</summary>
    public PaneNode Target { get; }

    /// <summary>Which side of the target, or <see cref="DockZone.Center"/> for "as another tab".</summary>
    public DockZone Zone { get; }

    /// <summary>Set true to refuse: nothing moves and the floating window stays where it is.</summary>
    public bool Cancel { get; set; }
}
