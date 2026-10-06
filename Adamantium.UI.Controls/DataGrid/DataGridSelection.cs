using System;
using System.Collections.Generic;
using System.Text;

namespace Adamantium.UI.Controls.DataGrid;

/// <summary>A rectangle of cells, in row and column INDICES. Indices and not containers: a selected cell usually has no
/// container - it is off screen, which is the ordinary case on a table - and indices survive scrolling and recycling.</summary>
public readonly struct CellRange : IEquatable<CellRange>
{
    public CellRange(int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        FirstRow = Math.Min(firstRow, lastRow);
        LastRow = Math.Max(firstRow, lastRow);
        FirstColumn = Math.Min(firstColumn, lastColumn);
        LastColumn = Math.Max(firstColumn, lastColumn);
    }

    public int FirstRow { get; }
    public int FirstColumn { get; }
    public int LastRow { get; }
    public int LastColumn { get; }

    public bool Contains(int row, int column) =>
        row >= FirstRow && row <= LastRow && column >= FirstColumn && column <= LastColumn;

    public bool Equals(CellRange other) =>
        FirstRow == other.FirstRow && LastRow == other.LastRow &&
        FirstColumn == other.FirstColumn && LastColumn == other.LastColumn;

    public override bool Equals(object obj) => obj is CellRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FirstRow, FirstColumn, LastRow, LastColumn);

    public override string ToString() => $"[{FirstRow},{FirstColumn}]..[{LastRow},{LastColumn}]";
}

/// <summary>What is selected, held as RANGES rather than as cells. Selecting a column of a million rows is one rectangle,
/// not a million entries - storing it per cell would spend memory exactly on the case a data grid exists for.
/// <para>Asking whether a cell is selected walks the ranges, and there are a handful of them.</para></summary>
public class DataGridSelection
{
    private readonly List<CellRange> _ranges = new();

    public IReadOnlyList<CellRange> Ranges => _ranges;

    public bool IsEmpty => _ranges.Count == 0;

    public event EventHandler Changed;

    /// <summary>Whether ANY cell of this row is selected - what a whole-row operation asks.</summary>
    public bool ContainsRow(int row)
    {
        foreach (var range in _ranges)
        {
            if (row >= range.FirstRow && row <= range.LastRow) return true;
        }

        return false;
    }

    public bool Contains(int row, int column)
    {
        foreach (var range in _ranges)
        {
            if (range.Contains(row, column)) return true;
        }

        return false;
    }

    /// <summary>Replaces everything with one rectangle - a plain click.</summary>
    public void Set(CellRange range)
    {
        _ranges.Clear();
        _ranges.Add(range);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds a rectangle beside what is already selected - Ctrl+click, and the reason this is a LIST of ranges
    /// rather than one.</summary>
    public void Add(CellRange range)
    {
        _ranges.Add(range);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes a rectangle out of what is selected: every range it crosses keeps what lies above, below and to
    /// either side of it.</summary>
    public void Remove(CellRange cut)
    {
        var kept = new List<CellRange>(_ranges.Count + 4);
        var changed = false;
        foreach (var range in _ranges)
        {
            var top = Math.Max(range.FirstRow, cut.FirstRow);
            var bottom = Math.Min(range.LastRow, cut.LastRow);
            var left = Math.Max(range.FirstColumn, cut.FirstColumn);
            var right = Math.Min(range.LastColumn, cut.LastColumn);
            if (top > bottom || left > right)
            {
                kept.Add(range);
                continue;
            }

            changed = true;
            if (range.FirstRow < top) kept.Add(new CellRange(range.FirstRow, range.FirstColumn, top - 1, range.LastColumn));
            if (range.LastRow > bottom) kept.Add(new CellRange(bottom + 1, range.FirstColumn, range.LastRow, range.LastColumn));
            if (range.FirstColumn < left) kept.Add(new CellRange(top, range.FirstColumn, bottom, left - 1));
            if (range.LastColumn > right) kept.Add(new CellRange(top, right + 1, bottom, range.LastColumn));
        }

        if (!changed) return;

        _ranges.Clear();
        _ranges.AddRange(kept);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Grows the last rectangle to reach a cell - Shift+click, and Shift+arrows.</summary>
    public void ExtendTo(int anchorRow, int anchorColumn, int row, int column)
    {
        var range = new CellRange(anchorRow, anchorColumn, row, column);
        if (_ranges.Count == 0) _ranges.Add(range);
        else _ranges[^1] = range;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_ranges.Count == 0) return;
        _ranges.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The smallest rectangle covering everything selected - what a copy walks.</summary>
    public bool TryGetBounds(out CellRange bounds)
    {
        bounds = default;
        if (_ranges.Count == 0) return false;

        var firstRow = int.MaxValue;
        var firstColumn = int.MaxValue;
        var lastRow = int.MinValue;
        var lastColumn = int.MinValue;

        foreach (var range in _ranges)
        {
            firstRow = Math.Min(firstRow, range.FirstRow);
            firstColumn = Math.Min(firstColumn, range.FirstColumn);
            lastRow = Math.Max(lastRow, range.LastRow);
            lastColumn = Math.Max(lastColumn, range.LastColumn);
        }

        bounds = new CellRange(firstRow, firstColumn, lastRow, lastColumn);
        return true;
    }
}
