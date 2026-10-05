using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>Stands for a row of a <see cref="TreeDataGrid"/> the virtualizing rows have not made: selected and brought
/// into view as any stand-in, and opened or closed when it has children.</summary>
public class TreeDataGridRowStandInAutomationPeer : ItemAutomationPeer, IExpandCollapseProvider, IGridItemProvider,
    ITableItemProvider
{
    private readonly TreeDataGrid _grid;
    private readonly TreeRow _row;

    internal TreeDataGridRowStandInAutomationPeer(TreeDataGridAutomationPeer gridPeer, TreeDataGrid grid, TreeRow row)
        : base(gridPeer, row, AutomationControlType.DataItem)
    {
        _grid = grid;
        _row = row;
    }

    public override string ClassName => nameof(DataGridRow);

    public ExpandCollapseState ExpandCollapseState => _row switch
    {
        { HasChildren: false } or null => ExpandCollapseState.LeafNode,
        { IsExpanded: true } => ExpandCollapseState.Expanded,
        _ => ExpandCollapseState.Collapsed
    };

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.ExpandCollapse && _row is not { HasChildren: true } ? null : base.GetPattern(pattern);

    public int Row => _grid.Items.IndexOf(_row);

    public int Column => 0;

    /// <summary>A row takes every column of its grid.</summary>
    public int ColumnSpan => Math.Max(1, _grid.Columns.Count);

    public AutomationPeer ContainingGrid => ItemsOwner;

    public IReadOnlyList<AutomationPeer> GetRowHeaders() => [];

    public IReadOnlyList<AutomationPeer> GetColumnHeaders() => (ItemsOwner as ITableProvider)?.GetColumnHeaders() ?? [];

    public void Expand() => _grid.ExpandRow(_row);

    public void Collapse() => _grid.CollapseRow(_row);
}
