using System.Collections.Generic;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Docking;

/// <summary>One root of a layout: a screen rectangle and the tree inside it. Main and floating windows are equal roots; this
/// rectangle is the only absolute geometry, the rest is fractions.</summary>
public class DockingRoot
{
    public DockingRoot(PaneNode content, bool isMain = false)
    {
        Content = content;
        IsMain = isMain;
    }

    /// <summary>True for the root that lives in the application's main window (there is at most one).</summary>
    public bool IsMain { get; set; }

    /// <summary>This window's document area, or null. A node rather than a flag, so a tool docked beside it keeps a tool's
    /// look.</summary>
    public PaneNode DocumentWell { get; set; }

    /// <summary>Where the window sits, in SCREEN coordinates. Restoring checks it against the available screens - a
    /// window saved on a monitor that is no longer there has to come back somewhere visible.</summary>
    public Rect Bounds { get; set; }

    public PaneNode Content { get; set; }

    /// <summary>The panels put away against each edge, shown as tab strips. Kept out of <see cref="Content"/> so nothing inside
    /// the tree disturbs them.</summary>
    public Dictionary<DockZone, List<PaneGroupNode>> Bars { get; } = new()
    {
        [DockZone.Left] = [],
        [DockZone.Top] = [],
        [DockZone.Right] = [],
        [DockZone.Bottom] = []
    };

    /// <summary>Which edge a put-away panel is on, or <see cref="DockZone.None"/> if it is not put away here.</summary>
    public DockZone EdgeOfBarred(PaneGroupNode group)
    {
        foreach (var pair in Bars)
        {
            if (pair.Value.Contains(group)) return pair.Key;
        }

        return DockZone.None;
    }
}
