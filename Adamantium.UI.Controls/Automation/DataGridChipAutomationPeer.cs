using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a chip in a data grid's sorting or grouping strip: a key, called by its column, moved along the
/// strip to change which key comes first, and taken out by its ×. A sorting key also turns around, as a press on it
/// does: on is descending.</summary>
public class DataGridChipAutomationPeer : ContentControlAutomationPeer, IToggleProvider, ITransformProvider
{
    private ChipRemoveAutomationPeer _remove;

    public DataGridChipAutomationPeer(ContentControl owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.DataItem;

    public ToggleState ToggleState => Owner is DataGridSortChip { IsDescending: true } ? ToggleState.On : ToggleState.Off;

    public void Toggle()
    {
        if (Owner is DataGridSortChip { Column: { } column } && Grid() is { } grid)
        {
            grid.FlipSort(column);
        }
    }

    public bool CanMove => Keys().Count > 1;

    public bool CanResize => false;

    public bool CanZoom => false;

    public double ZoomLevel => 100;

    public double ZoomMinimum => 100;

    public double ZoomMaximum => 100;

    /// <summary>Moves the chip so its left edge is at <paramref name="x"/>: it lands before the first chip whose middle
    /// lies past its own, as a chip carried along the strip and let go there does.</summary>
    public void Move(double x, double y)
    {
        var keys = Keys();
        var from = keys.IndexOf(Owner);
        if (from < 0 || Grid() is not { } grid)
        {
            throw new InvalidOperationException($"'{Name}' is not in a strip.");
        }

        var middle = x + BoundingRectangle.Width / 2;
        var target = keys.Count;
        for (var i = 0; i < keys.Count; i++)
        {
            var bounds = keys[i].GetAutomationPeer().BoundingRectangle;
            if (middle < bounds.X + bounds.Width / 2)
            {
                target = i;
                break;
            }
        }

        var to = Math.Clamp(target > from ? target - 1 : target, 0, keys.Count - 1);
        if (Owner is DataGridSortChip)
        {
            grid.MoveSorting(from, to);
        }
        else
        {
            grid.MoveGrouping(from, to);
        }
    }

    public void Resize(double width, double height) => throw new InvalidOperationException("A chip is not resized.");

    public void Zoom(double percent) => throw new InvalidOperationException("A chip is not zoomed.");

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.Toggle && Owner is not DataGridSortChip ? null : base.GetPattern(pattern);

    /// <summary>Its × only: what the chip shows is its name.</summary>
    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var part = Owner switch
        {
            DataGridSortChip sort => sort.RemovePart,
            DataGridGroupChip group => group.RemovePart,
            _ => null
        };

        if (part is not UIComponent remove)
        {
            return [];
        }

        if (_remove?.Owner != remove)
        {
            _remove = new ChipRemoveAutomationPeer(remove, Remove);
        }

        return [_remove];
    }

    private void Remove()
    {
        if (Grid() is not { } grid)
        {
            return;
        }

        switch (Owner)
        {
            case DataGridSortChip { Column: { } column }:
                grid.RemoveSort(column);
                break;
            case DataGridGroupChip { Column: { } column }:
                grid.GroupBy(column);
                break;
        }
    }

    private List<UIComponent> Keys() => Owner.GetVisualAncestors().FirstOrDefault(a => a is DataGridSortPanel or DataGridGroupPanel) switch
    {
        DataGridSortPanel sort => [.. sort.Chips],
        DataGridGroupPanel group => [.. group.Chips],
        _ => []
    };

    private TreeDataGrid Grid() => Owner.GetVisualAncestors().FirstOrDefault(a => a is DataGridSortPanel or DataGridGroupPanel) switch
    {
        DataGridSortPanel sort => sort.Owner,
        DataGridGroupPanel group => group.Owner,
        _ => null
    };
}
