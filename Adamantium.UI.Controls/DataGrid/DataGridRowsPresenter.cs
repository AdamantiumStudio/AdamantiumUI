using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DataGrid;

/// <summary>The rows' host: a vertical virtualizing stack that also tells the scroller how WIDE the table is.
/// <para>It has to, because a row cannot: a desired size is clamped to the slot it was measured in, so a row asked to
/// measure inside a 600-pixel viewport reports 600 however many columns it holds, and the scroller concludes there is
/// nothing to scroll sideways. The columns' total is the one honest number, and the grid already has it.</para></summary>
public class DataGridRowsPresenter : StackPanel
{
    /// <summary>The rows that are not rows: a record's details panel stands at its own height while everything else
    /// takes the row height. There are a handful of them at most - a table is opened at three records, not ten
    /// thousand - which is why the stack stays uniform arithmetic with a short list of exceptions rather than a walk.</summary>
    protected override IReadOnlyList<(int Index, double Extra)> ItemExtentExceptions =>
        Grid()?.RowExtentExceptions;

    protected override Size MeasureVirtualized(Size availableSize, Vector2 offset)
    {
        var extent = base.MeasureVirtualized(availableSize, offset);
        var width = Grid()?.ColumnsWidth ?? 0;
        if (width <= extent.Width) return extent;

        // Wider than the view means a horizontal scrollbar, and the bar is drawn OVER the content: without a tail of
        // blank the last row is permanently half-covered by it - and with a placeholder at the bottom, that is the row
        // the table most needs reachable.
        return new Size(width, extent.Height + Math.Max(0, Grid()?.EndPadding ?? 0));
    }

    protected override void ArrangeVirtualized(Size finalSize, Vector2 offset)
    {
        var width = Grid()?.ColumnsWidth ?? 0;
        base.ArrangeVirtualized(width > finalSize.Width ? new Size(width, finalSize.Height) : finalSize, offset);
    }

    private TreeDataGrid Grid() => Owner as TreeDataGrid;
}
