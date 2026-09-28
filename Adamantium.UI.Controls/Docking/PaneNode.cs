namespace Adamantium.UI.Controls.Docking;

/// <summary>A node of a docking layout - a split or a group, and only those two. Plain data, so layouts can be tested without
/// a window.</summary>
public abstract class PaneNode
{
    /// <summary>How much of its parent split this node takes: pixels, or a weight of what is left; ignored on a root. One
    /// number, kept on the child so it travels with it.</summary>
    public Panels.PaneLength Length { get; set; } = Panels.PaneLength.Star;

    /// <summary>The split this node hangs from, or null for a root's top node.</summary>
    public PaneSplitNode Parent { get; internal set; }
}
