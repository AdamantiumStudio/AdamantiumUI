using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DataGridCell"/>: a cell of its grid, called by what it shows. Its value is written as
/// committing an edit writes it - through the grid's events and rules and the column's binding. Text it shows is not
/// a child; anything a template puts in it to act on is.</summary>
public class DataGridCellAutomationPeer : ContentControlAutomationPeer, IValueProvider, IGridItemProvider, ISelectionItemProvider
{
    private readonly DataGridCell _cell;

    public DataGridCellAutomationPeer(DataGridCell owner) : base(owner)
    {
        _cell = owner;
    }

    public override AutomationControlType ControlType =>
        IsReadOnly ? AutomationControlType.Text : AutomationControlType.Edit;

    public string Value => TextOf(_cell) ?? string.Empty;

    public bool IsReadOnly => Grid()?.IsCellReadOnly(Row, Column) ?? true;

    public int Row => _cell.GetVisualAncestors().OfType<DataGridRow>().FirstOrDefault() is { } row
        ? Grid()?.Items.IndexOf(row.Row) ?? -1
        : -1;

    public int Column => _cell.ColumnIndex;

    public AutomationPeer ContainingGrid => Grid()?.GetAutomationPeer();

    public bool IsSelected => _cell.IsSelected;

    public AutomationPeer SelectionContainer => ContainingGrid;

    /// <summary>Writes the text as the cell's value; refused, as an edit would be, when the grid says no.</summary>
    public void SetValue(string value)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"The cell at row {Row}, column {Column} is read-only.");
        }

        var grid = Grid();
        if (grid?.WriteCell(Row, Column, value) != true)
        {
            throw new InvalidOperationException($"The grid refused '{value}' at row {Row}, column {Column}.");
        }

        grid.RefreshRealizedRows();
    }

    public void Select() => Grid()?.SelectCell(Row, Column);

    protected override string NameCore() => TextOf(_cell);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var children = new List<AutomationPeer>();
        Collect(_cell, children);
        return [.. children.Where(child => child.ControlType != AutomationControlType.Text)];
    }

    private TreeDataGrid Grid() => _cell.GetVisualAncestors().OfType<TreeDataGrid>().FirstOrDefault();
}
