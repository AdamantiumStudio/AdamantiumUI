using System.Collections.Generic;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a row of a <see cref="TreeView"/>: a tree item, expanded and selected the way its expander and a
/// click do it, with the rows of its branch as children.</summary>
public class TreeViewItemAutomationPeer : UIComponentAutomationPeer, ISelectionItemProvider, IExpandCollapseProvider, IScrollItemProvider
{
    public TreeViewItemAutomationPeer(TreeViewItem owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.TreeItem;

    public bool IsSelected => ((TreeViewItem)Owner).IsSelected;

    public AutomationPeer SelectionContainer => Tree()?.GetAutomationPeer();

    public ExpandCollapseState ExpandCollapseState => ((TreeViewItem)Owner).Row switch
    {
        null or { HasChildren: false } => ExpandCollapseState.LeafNode,
        { IsExpanded: true } => ExpandCollapseState.Expanded,
        _ => ExpandCollapseState.Collapsed
    };

    public override AutomationPeer GetParent() =>
        TreePeer() is { } tree && ((TreeViewItem)Owner).Row is { } row ? tree.ParentOf(row) : base.GetParent();

    public void Select()
    {
        var item = (TreeViewItem)Owner;
        item.FindOwnerTreeView()?.OnItemClicked(item, InputModifiers.None);
    }

    public void Expand() => Owner.SetCurrentValue(TreeViewItem.IsExpandedProperty, true);

    public void Collapse() => Owner.SetCurrentValue(TreeViewItem.IsExpandedProperty, false);

    public void ScrollIntoView() => Owner.BringIntoView();

    protected override string NameCore() => TextOf(Owner);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() =>
        TreePeer() is { } tree && ((TreeViewItem)Owner).Row is { } row ? tree.ChildrenOf(row) : [];

    private TreeView Tree() => ((TreeViewItem)Owner).FindOwnerTreeView();

    private TreeViewAutomationPeer TreePeer() => Tree()?.GetAutomationPeer() as TreeViewAutomationPeer;
}
