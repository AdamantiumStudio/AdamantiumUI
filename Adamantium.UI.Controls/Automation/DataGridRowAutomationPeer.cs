using System.Collections.Generic;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DataGridRow"/>: a data item called by its value in the first column, whose children
/// are its cells. It is selected as a click on its header selects it, and a row with children opens and closes.</summary>
public class DataGridRowAutomationPeer : UIComponentAutomationPeer, ISelectionItemProvider, IExpandCollapseProvider
{
    private readonly DataGridRow _row;

    public DataGridRowAutomationPeer(DataGridRow owner) : base(owner)
    {
        _row = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.DataItem;

    /// <summary>Every cell of the row taken - what the number strip shows as a selected row.</summary>
    public bool IsSelected => _row.Owner?.IsRowFullySelected(Index) == true;

    public AutomationPeer SelectionContainer => _row.Owner?.GetAutomationPeer();

    public ExpandCollapseState ExpandCollapseState => _row.Row switch
    {
        { HasChildren: false } or null => ExpandCollapseState.LeafNode,
        { IsExpanded: true } => ExpandCollapseState.Expanded,
        _ => ExpandCollapseState.Collapsed
    };

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.ExpandCollapse && _row.Row is not { HasChildren: true } ? null : base.GetPattern(pattern);

    public void Select() => _row.Owner?.SelectRow(Index);

    public void Expand() => _row.Owner?.ExpandRow(_row.Row);

    public void Collapse() => _row.Owner?.CollapseRow(_row.Row);

    /// <summary>Where the row stands among the rows shown.</summary>
    public int Index => _row.Owner?.Items.IndexOf(_row.Row) ?? -1;

    protected override string NameCore() =>
        (_row.Owner?.GetAutomationPeer() as TreeDataGridAutomationPeer)?.NameOfItem(_row.Row);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var children = new List<AutomationPeer>();
        Collect(_row, children);
        return children;
    }
}
