using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Docking;

/// <summary>Where a dragged pane would land: the target node, the side, and the resulting rectangle, one answer for preview
/// and drop. A node, so an edge anchor is the same move aimed at the root.</summary>
public readonly struct DockTarget
{
    public DockTarget(PaneNode node, Rect bounds, DockZone zone, Rect preview, bool isEdge = false)
    {
        Node = node;
        Bounds = bounds;
        Zone = zone;
        Preview = preview;
        IsEdge = isEdge;
    }

    /// <summary>What the drop is aimed at: a group, or the area's root node for an edge anchor.</summary>
    public PaneNode Node { get; }

    /// <summary>The group under the pointer, in the docking area's coordinates. The compass draws its cross from the
    /// centre of it - the same centre the indicators are measured from. It stays the group even when an EDGE is armed:
    /// the cross belongs where the pointer is, only the answer is about the whole area.</summary>
    public Rect Bounds { get; }

    /// <summary><see cref="DockZone.None"/> means the pointer is not over any indicator - dropping does nothing.</summary>
    public DockZone Zone { get; }

    /// <summary>Whether an EDGE anchor is armed rather than one of the cross's five. Only the compass needs it, to pick
    /// which rectangle to preview; the drop already holds the node it is to split.</summary>
    public bool IsEdge { get; }

    /// <summary>Where the pane would be, in the docking area's own coordinates.</summary>
    public Rect Preview { get; }

    public bool IsValid => Node != null && Zone != DockZone.None;
}
