namespace Adamantium.UI.Core.Automation;

/// <summary>Whether what an element holds is open.</summary>
public enum ExpandCollapseState
{
    Collapsed,
    Expanded,

    /// <summary>It holds nothing to open, such as a menu item without a submenu or a branch without children.</summary>
    LeafNode
}
