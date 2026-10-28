using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Panels;

/// <summary>The items host of a <see cref="RibbonGallery"/>: equal cells in a grid of <see cref="Columns"/> columns, of
/// which <see cref="Rows"/> are shown at a time starting at <see cref="FirstRow"/>. Equal cells and not a WrapPanel:
/// a gallery is a grid of CHOICES, and choices that shift sideways because one thumbnail is wider cannot be compared.</summary>
public class RibbonGalleryPanel : Panel
{
    public static readonly AdamantiumProperty ColumnsProperty = AdamantiumProperty.Register(nameof(Columns),
        typeof(int), typeof(RibbonGalleryPanel),
        new PropertyMetadata(5, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

    public static readonly AdamantiumProperty RowsProperty = AdamantiumProperty.Register(nameof(Rows),
        typeof(int), typeof(RibbonGalleryPanel),
        new PropertyMetadata(1, PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

    public static readonly AdamantiumProperty FirstRowProperty = AdamantiumProperty.Register(nameof(FirstRow),
        typeof(int), typeof(RibbonGalleryPanel),
        new PropertyMetadata(0, PropertyMetadataOptions.AffectsArrange));

    /// <summary>How many choices stand side by side.</summary>
    public int Columns
    {
        get => GetValue<int>(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>How many rows are shown at once. The rest are reached with the scroll arrows or in the drop-down.</summary>
    public int Rows
    {
        get => GetValue<int>(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>The topmost shown row. Rows, not pixels: a half-row reads as damage, exactly as a half-group does in
    /// <see cref="RibbonGroupsPanel"/>.</summary>
    public int FirstRow
    {
        get => GetValue<int>(FirstRowProperty);
        set => SetValue(FirstRowProperty, value);
    }

    /// <summary>How many rows the items make at the current <see cref="Columns"/> - what the gallery's arrows are
    /// decided by. A <see cref="RibbonGalleryGroupHeader"/> is a row of its own.</summary>
    public int RowCount
    {
        get
        {
            var rows = 0;
            Place((_, row, _, _) => rows = row + 1);
            return rows;
        }
    }

    private double _cellWidth;
    private double _cellHeight;

    // The top and height of each row, as the last measure laid them.
    private readonly System.Collections.Generic.List<(double Top, double Height)> _rows = [];

    /// <summary>A GRID: the generic order-based walk would make Right at the end of a row jump a row down without
    /// Down ever working. Group headers take no focus, so Up and Down step over them.</summary>
    public override IUIComponent Navigate(IUIComponent from, FocusNavigationDirection direction)
    {
        if (!IsArrow(direction)) return base.Navigate(from, direction);

        if (from is not IMeasurableComponent measurable) return null;

        var (row, column) = (-1, -1);
        Place((child, r, c, _) =>
        {
            if (ReferenceEquals(child, measurable)) (row, column) = (r, c);
        });
        if (row < 0) return null;

        // Left/Right stay inside the row - leaving it is what Tab is for.
        if (!IsVertical(direction))
        {
            return CellAt(row, column + (IsForward(direction) ? 1 : -1));
        }

        // The next row of cells, over titles: the same column there, or nothing - a shorter last row stops the arrow.
        var step = IsForward(direction) ? 1 : -1;
        var rows = RowCount;
        for (var next = row + step; next >= 0 && next < rows; next += step)
        {
            if (!IsTitleRow(next)) return CellAt(next, column);
        }

        return null;
    }

    private IUIComponent CellAt(int row, int column)
    {
        IUIComponent found = null;
        Place((child, r, c, header) =>
        {
            if (r == row && c == column && !header) found = child;
        });
        return found;
    }

    private bool IsTitleRow(int row)
    {
        var title = false;
        Place((_, r, _, header) => title |= r == row && header);
        return title;
    }

    // Walks the shown children in order, saying where each stands: a header takes a row of its own, and the cells after
    // it begin a new one.
    private void Place(Action<IMeasurableComponent, int, int, bool> at)
    {
        var columns = Math.Max(1, Columns);
        var row = 0;
        var column = 0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;

            if (child is RibbonGalleryGroupHeader)
            {
                if (column > 0) row++;
                at(child, row, 0, true);
                row++;
                column = 0;
                continue;
            }

            at(child, row, column, false);
            if (++column == columns)
            {
                row++;
                column = 0;
            }
        }
    }

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        _cellWidth = 0;
        _cellHeight = 0;
        var cells = 0;
        var headerWidth = 0.0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;

            child.Measure(Size<double>.Infinity);
            if (child is RibbonGalleryGroupHeader)
            {
                headerWidth = Math.Max(headerWidth, child.DesiredSize.Width);
                continue;
            }

            cells++;
            _cellWidth = Math.Max(_cellWidth, child.DesiredSize.Width);
            _cellHeight = Math.Max(_cellHeight, child.DesiredSize.Height);
        }

        _rows.Clear();
        var top = 0.0;
        Place((child, row, _, header) =>
        {
            if (row < _rows.Count) return;

            var rowHeight = header ? child.DesiredSize.Height : _cellHeight;
            _rows.Add((top, rowHeight));
            top += rowHeight;
        });

        var columns = Math.Min(Math.Max(1, Columns), Math.Max(1, cells));
        var shown = Math.Min(Math.Max(1, Rows), _rows.Count);
        var height = shown > 0 ? _rows[shown - 1].Top + _rows[shown - 1].Height : _cellHeight;

        return new Size<double>(Math.Max(columns * _cellWidth, headerWidth), height);
    }

    private double _arrangedHeight;

    /// <summary>How many rows fit WHOLE in the height this panel was GIVEN - which is not always the <see cref="Rows"/>
    /// asked for, and is what "scrolled to the end" has to be measured against.</summary>
    public int VisibleRows => _cellHeight <= 0 || _arrangedHeight <= 0
        ? Math.Max(1, Rows)
        : Math.Max(1, (int)(_arrangedHeight / _cellHeight));

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        _arrangedHeight = finalSize.Height;

        if (_rows.Count == 0)
        {
            return finalSize;
        }

        // Never past the end: the last row lands flush with the bottom rather than scrolling into empty space.
        var end = _rows[^1].Top + _rows[^1].Height;
        var maxTop = Math.Max(0, end - finalSize.Height);
        var top = Math.Min(_rows[Math.Clamp(FirstRow, 0, _rows.Count - 1)].Top, maxTop);
        var width = finalSize.Width;

        // Every cell is arranged, scrolled-away rows included: the gallery clips them. Arranging them to nothing would
        // make their measured width zero, and a zero always fits - the same trap the quick-access overflow hit.
        Place((child, row, column, header) =>
        {
            if (row >= _rows.Count) return;

            var (rowTop, height) = _rows[row];
            child.Arrange(header
                ? new Rect(0, rowTop - top, width, height)
                : new Rect(column * _cellWidth, rowTop - top, _cellWidth, height));
        });

        return finalSize;
    }
}
