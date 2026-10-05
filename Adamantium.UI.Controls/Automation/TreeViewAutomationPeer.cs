using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="TreeView"/>. Its rows are laid out flat, a branch's children right after it and one
/// level deeper; automation sees them nested again - each row's children are the rows below it, one level deeper - with
/// the row's element where it has one and a stand-in where it has none yet.</summary>
public class TreeViewAutomationPeer : UIComponentAutomationPeer, ISelectionProvider
{
    private readonly Dictionary<TreeRow, TreeRowAutomationPeer> _rowPeers = new();

    public TreeViewAutomationPeer(TreeView owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Tree;

    public bool CanSelectMultiple => ((TreeView)Owner).SelectionMode != TreeViewSelectionMode.Single;

    public IReadOnlyList<AutomationPeer> GetSelection()
    {
        var rows = Rows();
        return [.. rows.Select((row, index) => (row, index)).Where(pair => pair.row.IsSelected).Select(pair => PeerOf(pair.index, pair.row))];
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => ChildrenOf(null);

    internal IReadOnlyList<AutomationPeer> ChildrenOf(TreeRow parent)
    {
        var rows = Rows();
        var children = new List<AutomationPeer>();
        var start = parent == null ? 0 : rows.IndexOf(parent) + 1;
        if (parent != null && start == 0)
        {
            return children;
        }

        var depth = parent == null ? 0 : parent.Depth + 1;
        for (var index = start; index < rows.Count; index++)
        {
            var row = rows[index];
            if (parent != null && row.Depth < depth)
            {
                break;
            }

            if (row.Depth == depth)
            {
                children.Add(PeerOf(index, row));
            }
        }

        return children;
    }

    internal AutomationPeer ParentOf(TreeRow row)
    {
        var rows = Rows();
        for (var index = rows.IndexOf(row) - 1; index >= 0; index--)
        {
            if (rows[index].Depth == row.Depth - 1)
            {
                return PeerOf(index, rows[index]);
            }
        }

        return this;
    }

    private AutomationPeer PeerOf(int index, TreeRow row)
    {
        if (((TreeView)Owner).ItemContainerGenerator.ContainerFromIndex(index) is TreeViewItem container
            && ReferenceEquals(container.Row, row))
        {
            return container.GetAutomationPeer();
        }

        if (!_rowPeers.TryGetValue(row, out var peer))
        {
            peer = new TreeRowAutomationPeer(this, row);
            _rowPeers[row] = peer;
        }

        return peer;
    }

    private List<TreeRow> Rows() => [.. ((TreeView)Owner).Items.OfType<TreeRow>()];
}
