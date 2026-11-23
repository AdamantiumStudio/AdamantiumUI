using System;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.DataGrid;

/// <summary>A column's width dragged by the line on its right: from its header, or from the rows while the table shows
/// no headers. Positions are in the table's viewport, as the header strip lays the columns out; double-clicking the line
/// fits the column to what is on screen.</summary>
internal sealed class DataGridColumnResize
{
    /// <summary>How close to a column's line the pointer counts as being on it.</summary>
    private const double GripWidth = 6;

    private readonly TreeDataGrid _owner;
    private int _resizing = -1;
    private double _resizeFrom;
    private double _startWidth;

    public DataGridColumnResize(TreeDataGrid owner)
    {
        _owner = owner;
    }

    /// <summary>Whether a width is being dragged.</summary>
    public bool IsResizing => _resizing >= 0;

    /// <summary>The column whose right-hand line is under <paramref name="x"/>, or -1.</summary>
    public int SeparatorAt(double x)
    {
        var columns = _owner.Columns;
        if (columns == null) return -1;

        for (var pinned = 0; pinned < 2; pinned++)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                // PINNED columns first: they are drawn over what scrolls beneath them, so they are also what the pointer
                // reaches there.
                if (columns[i].IsFrozen != (pinned == 0)) continue;

                var edge = _owner.ScreenXOf(columns[i]) + columns[i].ActualWidth;
                if (Math.Abs(x - edge) <= GripWidth / 2) return i;
            }
        }

        return -1;
    }

    /// <summary>The cursor the pointer shows over <paramref name="x"/>: the resize one on a line that can be dragged.</summary>
    public Cursor CursorAt(double x)
    {
        var over = SeparatorAt(x);
        return over >= 0 && _owner.Columns[over].CanUserResize ? Cursors.SizeWE : null;
    }

    /// <summary>A press at <paramref name="x"/>: on a line that can be dragged, the drag begins - or, pressed twice, the
    /// column is fitted. Whether the press was taken.</summary>
    public bool Press(double x, int clickCount)
    {
        var separator = SeparatorAt(x);
        if (separator < 0 || !_owner.Columns[separator].CanUserResize) return false;

        // Double-click fits the column to what is on screen NOW - the only width it can honestly know, since measuring
        // the rows it has not built would mean building them.
        if (clickCount >= 2)
        {
            _owner.FitColumn(separator);
            return true;
        }

        _resizing = separator;
        _resizeFrom = x;
        _startWidth = _owner.Columns[separator].ActualWidth;
        return true;
    }

    /// <summary>The pointer moved to <paramref name="x"/> while dragging: the column takes the width.</summary>
    public void Move(double x)
    {
        if (_resizing < 0) return;

        var column = _owner.Columns[_resizing];
        var wanted = _startWidth + (x - _resizeFrom);
        // At the slot the width already sits in, not a fresh Local one: a page can bind a column's width, and a Local
        // write outranks Binding for good - one drag and that binding would never be heard from again.
        column.SetCurrentValue(DataGridColumn.WidthProperty, new GridLength(Math.Max(column.MinWidth, wanted)));
        _owner.InvalidateColumns();
    }

    /// <summary>The drag is over.</summary>
    public void Release() => _resizing = -1;
}
