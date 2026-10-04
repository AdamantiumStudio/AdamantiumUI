using System.Collections.Generic;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>Stands for a row of a <see cref="TreeView"/> that has no element yet. It can be expanded, selected and
/// brought into view, which makes its element.</summary>
public class TreeRowAutomationPeer : AutomationPeer, ISelectionItemProvider, IExpandCollapseProvider, IScrollItemProvider
{
    private readonly TreeViewAutomationPeer _tree;
    private readonly TreeRow _row;

    internal TreeRowAutomationPeer(TreeViewAutomationPeer tree, TreeRow row)
    {
        _tree = tree;
        _row = row;
    }

    /// <summary>The node of the tree's data the row shows.</summary>
    public object Node => _row.Node;

    public override AutomationControlType ControlType => AutomationControlType.TreeItem;

    public override string Name => Node as string ?? Node?.ToString() ?? string.Empty;

    public override string AutomationId => string.Empty;

    public override string HelpText => string.Empty;

    public override string ClassName => Node?.GetType().Name ?? "null";

    public override Rect BoundingRectangle => Rect.Empty;

    public override bool IsEnabled => _tree.IsEnabled;

    public override bool IsOffscreen => true;

    public override bool HasKeyboardFocus => false;

    public override bool IsKeyboardFocusable => false;

    public bool IsSelected => _row.IsSelected;

    public AutomationPeer SelectionContainer => _tree;

    public ExpandCollapseState ExpandCollapseState => _row switch
    {
        { HasChildren: false } => ExpandCollapseState.LeafNode,
        { IsExpanded: true } => ExpandCollapseState.Expanded,
        _ => ExpandCollapseState.Collapsed
    };

    public override IReadOnlyList<AutomationPeer> GetChildren() => _tree.ChildrenOf(_row);

    public override AutomationPeer GetParent() => _tree.ParentOf(_row);

    public override void SetFocus()
    {
    }

    public void Select() => _tree.Owner.SetCurrentValue(TreeView.SelectedItemProperty, Node);

    public void Expand() => ((TreeView)_tree.Owner).SetRowExpanded(_row, true);

    public void Collapse() => ((TreeView)_tree.Owner).SetRowExpanded(_row, false);

    public void ScrollIntoView() => ((TreeView)_tree.Owner).ScrollIntoView(_row);
}
