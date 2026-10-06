using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="TreeDataGrid"/>: a data grid whose children are its column headers, then its rows in
/// the order shown - a row's element where it has one, a stand-in where the virtualizing rows have not made it, called
/// by the row's value in the first column. Rows are selected, brought into view and, in a tree, opened by the grid's own
/// actions.</summary>
public class TreeDataGridAutomationPeer : ItemsControlAutomationPeer, IGridProvider, ITableProvider, ISelectionProvider
{
    private readonly TreeDataGrid _grid;

    public TreeDataGridAutomationPeer(TreeDataGrid owner) : base(owner)
    {
        _grid = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.DataGrid;

    public int RowCount => _grid.Items.Count;

    public int ColumnCount => _grid.Columns.Count;

    public bool CanSelectMultiple => true;

    public bool IsSelectionRequired => false;

    public AutomationPeer GetItem(int row, int column) => _grid.CellFor(row, column)?.GetAutomationPeer();

    public IReadOnlyList<AutomationPeer> GetColumnHeaders()
    {
        var headers = new List<AutomationPeer>();
        if (HeadersPresenter() is { } presenter)
        {
            Collect(presenter, headers);
        }

        return headers;
    }

    /// <summary>The header of the column at <paramref name="column"/>, as one peer; empty when it shows none.</summary>
    public IReadOnlyList<AutomationPeer> GetColumnHeaders(int column)
    {
        var shown = column >= 0 && column < _grid.Columns.Count ? _grid.Columns[column] : null;
        return [.. GetColumnHeaders().Where(peer =>
            peer is UIComponentAutomationPeer { Owner: DataGridColumnHeader header } && ReferenceEquals(header.Column, shown))];
    }

    public IReadOnlyList<AutomationPeer> GetSelection() =>
        [.. base.ChildrenCore().Where(child => child.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider { IsSelected: true })];

    protected override AutomationControlType ItemControlType => AutomationControlType.DataItem;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [.. GetColumnHeaders(), .. base.ChildrenCore()];

    protected override ItemAutomationPeer CreateItemPeer(object item) =>
        item is TreeRow row ? new TreeDataGridRowStandInAutomationPeer(this, _grid, row) : base.CreateItemPeer(item);

    /// <summary>A row's value in the first column shown, as text; a group's row is called by its key, a details panel by
    /// the row it details.</summary>
    protected internal override string NameOfItem(object item) => item is TreeRow { Node: var node }
        ? NameOfNode(node)
        : base.NameOfItem(item);

    private string NameOfNode(object node)
    {
        switch (node)
        {
            case DataGridGroup group:
                return Convert.ToString(group.Key, CultureInfo.CurrentCulture) ?? string.Empty;
            case DataGridRowDetails details:
                return NameOfNode(details.Item);
        }

        var column = _grid.Columns.FirstOrDefault(candidate => candidate.IsShown);
        var value = column == null ? null : column.Read(column.Binding, node) ?? column.ReadPath(node);
        return Convert.ToString(value ?? node, CultureInfo.CurrentCulture) ?? string.Empty;
    }

    protected internal override bool IsItemSelected(object item) => _grid.IsRowFullySelected(_grid.Items.IndexOf(item));

    protected internal override bool CanSelectItems => true;

    protected internal override void SelectItem(object item) => _grid.SelectRow(_grid.Items.IndexOf(item));

    protected internal override void AddItemToSelection(object item)
    {
        if (!IsItemSelected(item))
        {
            _grid.SelectRow(_grid.Items.IndexOf(item), add: true);
        }
    }

    protected internal override void RemoveItemFromSelection(object item) => _grid.DeselectRow(_grid.Items.IndexOf(item));

    protected internal override void ScrollItemIntoView(object item) => _grid.ScrollIntoView(_grid.Items.IndexOf(item));

    private UIComponent HeadersPresenter() =>
        _grid.GetVisualDescendants().OfType<DataGridHeadersPresenter>().FirstOrDefault();
}
