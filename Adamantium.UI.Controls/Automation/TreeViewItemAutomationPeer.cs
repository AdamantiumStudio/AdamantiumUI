using System.Collections.Generic;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a row of a <see cref="TreeView"/>: a tree item, expanded and selected the way its expander and a
/// click do it. Its children are what the row shows that can be acted on - a check box in its line, say; the text is
/// its name and the expander its own pattern - then the rows of its branch.</summary>
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

    /// <summary>Makes it the one selected node - in a tree that selects many as well, where a click would toggle it.
    /// </summary>
    public void Select() => Act((tree, row) => tree.SelectOnlyRow(row));

    public void AddToSelection() => Act((tree, row) => tree.AddRowToSelection(row));

    public void RemoveFromSelection() => Act((tree, row) => tree.RemoveRowFromSelection(row));

    private void Act(System.Action<TreeView, TreeRow> change)
    {
        var item = (TreeViewItem)Owner;
        if (item.FindOwnerTreeView() is { } tree && item.Row is { } row)
        {
            change(tree, row);
        }
    }

    public void Expand() => Owner.SetCurrentValue(TreeViewItem.IsExpandedProperty, true);

    public void Collapse() => Owner.SetCurrentValue(TreeViewItem.IsExpandedProperty, false);

    public void ScrollIntoView() => Owner.BringIntoView();

    protected override string NameCore() => TextOf(Owner);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var item = (TreeViewItem)Owner;
        var children = new List<AutomationPeer>();
        foreach (var shown in base.ChildrenCore())
        {
            var isExpander = shown is UIComponentAutomationPeer { Owner: var element } && element == item.Expander;
            if (!isExpander && shown.ControlType is not (AutomationControlType.Text or AutomationControlType.Image))
            {
                children.Add(shown);
            }
        }

        if (TreePeer() is { } tree && item.Row is { } row)
        {
            children.AddRange(tree.ChildrenOf(row));
        }

        return children;
    }

    private TreeView Tree() => ((TreeViewItem)Owner).FindOwnerTreeView();

    private TreeViewAutomationPeer TreePeer() => Tree()?.GetAutomationPeer() as TreeViewAutomationPeer;
}
