using System.Collections.Generic;
using Adamantium.UI.Controls.Generators;

namespace Adamantium.UI.Controls.Panels;

/// <summary>The lines of a panel that shows groups: each group a header line, then - while it is expanded - its items in
/// lines of <c>columns</c> cells: one column for a stack, as many as fit for a wrap panel. Items are uniform cells, a
/// header as long as its level's, so every position is arithmetic over the groups, never a walk over the items. A stack
/// can also have rows longer than the cell (a row's details): a short sorted list of exceptions. A drop gap is one more
/// cell in its group: the items from it on move along by one, and a full last line makes the group a line longer.</summary>
internal sealed class GroupLines
{
    private readonly List<int> _place = [];       // the header's place, or -1 for items before any header
    private readonly List<int> _firstItem = [];
    private readonly List<int> _itemCount = [];
    private readonly List<bool> _expanded = [];
    private readonly List<double> _header = [];
    private readonly List<double> _start = [];
    private readonly List<int> _groupOfPlace = [];
    private readonly List<ItemRange> _folded = [];

    private int _columns = 1;
    private double _lineExtent;
    private IReadOnlyList<(int Index, double Extra)> _extras;
    private int _gapGroup = -1;
    private int _gapCell;

    /// <summary>The length of all the lines along the scroll axis.</summary>
    public double Extent { get; private set; }

    /// <summary>The items of the folded groups, ascending: a window across them makes none of them.</summary>
    public IReadOnlyList<ItemRange> FoldedItems => _folded;

    /// <summary>Lays the lines out again: <paramref name="places"/> in order, <paramref name="itemCount"/> items in
    /// lines of <paramref name="columns"/>, a header as long as <paramref name="headerExtents"/> says for its level (the
    /// last for a level past them) and <paramref name="headerGap"/> after it, a line of items <paramref name="lineExtent"/>.
    /// <paramref name="extras"/> - rows longer than the line, ascending by index, as a stack's
    /// <c>ItemExtentExceptions</c> gives them - only with one column; null for none. A drop gap opens in the group of
    /// place <paramref name="gapPlace"/> before its item <paramref name="gapSlot"/>, while that group is expanded; -1
    /// for none.</summary>
    public void Build(IReadOnlyList<GroupHeaderPlace> places, int itemCount, int columns, IReadOnlyList<double> headerExtents,
        double headerGap, double lineExtent, IReadOnlyList<(int Index, double Extra)> extras, int gapPlace = -1,
        int gapSlot = 0)
    {
        _place.Clear();
        _firstItem.Clear();
        _itemCount.Clear();
        _expanded.Clear();
        _header.Clear();
        _start.Clear();
        _groupOfPlace.Clear();
        _folded.Clear();
        _columns = columns < 1 ? 1 : columns;
        _lineExtent = lineExtent;
        _extras = _columns == 1 && extras is { Count: > 0 } ? extras : null;
        _gapGroup = -1;

        var at = 0.0;
        var leading = places.Count > 0 ? places[0].BeforeItem : itemCount;
        if (leading > 0)
        {
            at = AddGroup(-1, 0, leading, true, 0, at);
        }

        for (var i = 0; i < places.Count; i++)
        {
            var place = places[i];
            var end = i + 1 < places.Count ? places[i + 1].BeforeItem : itemCount;
            var header = headerGap +
                         (headerExtents.Count == 0 ? 0 : headerExtents[System.Math.Min(place.Level, headerExtents.Count - 1)]);
            _groupOfPlace.Add(_place.Count);
            if (i == gapPlace && place.IsExpanded)
            {
                _gapGroup = _place.Count;
                _gapCell = System.Math.Clamp(gapSlot, 0, end - place.BeforeItem);
            }

            at = AddGroup(i, place.BeforeItem, end - place.BeforeItem, place.IsExpanded, header, at);
        }

        Extent = at;
    }

    private double AddGroup(int place, int firstItem, int itemCount, bool expanded, double header, double at)
    {
        _place.Add(place);
        _firstItem.Add(firstItem);
        _itemCount.Add(itemCount);
        _expanded.Add(expanded);
        _header.Add(header);
        _start.Add(at);
        if (!expanded && itemCount > 0)
        {
            _folded.Add(new ItemRange(firstItem, firstItem + itemCount - 1));
        }

        return at + header + ItemsExtent(_place.Count - 1);
    }

    private int ShownCount(int group) => _expanded[group] ? _itemCount[group] : 0;

    // The cells the group's lines hold: its items shown, and the drop gap if it is there.
    private int CellCount(int group) => ShownCount(group) + (group == _gapGroup ? 1 : 0);

    // The cell of the group's item `inGroup`: past the gap, one along.
    private int CellOf(int group, int inGroup) => group == _gapGroup && inGroup >= _gapCell ? inGroup + 1 : inGroup;

    private int LinesOf(int cellCount) => (cellCount + _columns - 1) / _columns;

    // How long the group's items are: its lines, and what its longer rows add.
    private double ItemsExtent(int group)
    {
        var shown = ShownCount(group);
        return LinesOf(CellCount(group)) * _lineExtent + ExtraBetween(_firstItem[group], _firstItem[group] + shown);
    }

    // What the longer rows from item `from` up to (not including) `to` add.
    private double ExtraBetween(int from, int to)
    {
        if (_extras == null || to <= from)
        {
            return 0;
        }

        double extra = 0;
        for (var i = 0; i < _extras.Count && _extras[i].Index < to; i++)
        {
            if (_extras[i].Index >= from)
            {
                extra += _extras[i].Extra;
            }
        }

        return extra;
    }

    /// <summary>How long the item at <paramref name="index"/> is along the scroll axis: the line, and its extra.</summary>
    public double ItemExtent(int index) => _lineExtent + ExtraBetween(index, index + 1);

    /// <summary>Where along the scroll axis the item at <paramref name="index"/> starts, and its column; an item of a
    /// folded group stands where its group's header does.</summary>
    public double ItemStart(int index, out int column)
    {
        column = 0;
        var group = GroupOfItem(index);
        if (group < 0)
        {
            return 0;
        }

        if (!_expanded[group])
        {
            return _start[group];
        }

        var cell = CellOf(group, index - _firstItem[group]);
        column = cell % _columns;
        return _start[group] + _header[group] + cell / _columns * _lineExtent + ExtraBetween(_firstItem[group], index);
    }

    /// <summary>Where along the scroll axis the open drop gap starts, and its column; false while there is none.</summary>
    public bool TryGetGapStart(out double start, out int column)
    {
        start = 0;
        column = 0;
        if (_gapGroup < 0)
        {
            return false;
        }

        var first = _firstItem[_gapGroup];
        column = _gapCell % _columns;
        start = _start[_gapGroup] + _header[_gapGroup] + _gapCell / _columns * _lineExtent +
                ExtraBetween(first, first + _gapCell);
        return true;
    }

    /// <summary>The group a drop at <paramref name="along"/> the scroll axis, in <paramref name="column"/>, lands in,
    /// and before which of its items: the cell under it - the gap's own while it is open there - or past the last item,
    /// the group's end. On a header: the start of an open group (of its first inner group, for an outer one), the end of
    /// a folded one. False with no lines, or over items before any header.</summary>
    public bool TryGetDropSlot(double along, int column, out GroupDropSlot slot)
    {
        slot = default;
        if (_place.Count == 0)
        {
            return false;
        }

        var group = FirstGroupEndingAfter(along);
        var itemsStart = _start[group] + _header[group];
        int at;
        if (along < itemsStart)
        {
            // An outer header has no items of its own: an open one passes the drop to its first inner group.
            while (_expanded[group] && _itemCount[group] == 0 && group + 1 < _place.Count &&
                   _firstItem[group + 1] == _firstItem[group])
            {
                group++;
            }

            at = _expanded[group] ? 0 : _itemCount[group];
        }
        else
        {
            var line = System.Math.Max(0, LineAt(group, along - itemsStart));
            var cell = line * _columns + System.Math.Clamp(column, 0, _columns - 1);
            at = System.Math.Min(cell, _itemCount[group]);
        }

        // Items before any header are in no group a drop could join.
        if (_place[group] < 0)
        {
            return false;
        }

        slot = new GroupDropSlot(_place[group], _firstItem[group], _itemCount[group], at);
        return true;
    }

    /// <summary>Where along the scroll axis the header of place <paramref name="place"/> starts.</summary>
    public double HeaderStart(int place) =>
        place >= 0 && place < _groupOfPlace.Count ? _start[_groupOfPlace[place]] : 0;

    /// <summary>How long the header of place <paramref name="place"/> is.</summary>
    public double HeaderExtent(int place) =>
        place >= 0 && place < _groupOfPlace.Count ? _header[_groupOfPlace[place]] : 0;

    /// <summary>The items and the header places with a line between <paramref name="from"/> and <paramref name="to"/>
    /// along the scroll axis.</summary>
    public LinesWindow Window(double from, double to)
    {
        var firstItem = int.MaxValue;
        var lastItem = -1;
        var firstPlace = int.MaxValue;
        var lastPlace = -1;
        for (var group = FirstGroupEndingAfter(from); group < _place.Count && _start[group] <= to; group++)
        {
            var start = _start[group];
            var header = _header[group];
            if (_place[group] >= 0 && start + header > from)
            {
                firstPlace = System.Math.Min(firstPlace, _place[group]);
                lastPlace = System.Math.Max(lastPlace, _place[group]);
            }

            var shown = ShownCount(group);
            if (shown == 0 || _lineExtent <= 0)
            {
                continue;
            }

            var itemsStart = start + header;
            var fromLine = System.Math.Max(0, LineAt(group, from - itemsStart));
            var toLine = System.Math.Min(LinesOf(CellCount(group)) - 1, LineAt(group, to - itemsStart));
            if (toLine < fromLine)
            {
                continue;
            }

            var first = System.Math.Min(shown - 1, ItemFromCell(group, fromLine * _columns, after: true));
            var last = System.Math.Min(shown - 1, ItemFromCell(group, (toLine + 1) * _columns - 1, after: false));
            if (last < first)
            {
                continue;   // only the gap in view
            }

            firstItem = System.Math.Min(firstItem, _firstItem[group] + first);
            lastItem = System.Math.Max(lastItem, _firstItem[group] + last);
        }

        return new LinesWindow(firstItem == int.MaxValue ? 0 : firstItem, lastItem,
            firstPlace == int.MaxValue ? 0 : firstPlace, lastPlace);
    }

    // The group's item in `cell`; the gap's cell gives the item after it, or the one before.
    private int ItemFromCell(int group, int cell, bool after)
    {
        if (group != _gapGroup || cell < _gapCell)
        {
            return cell;
        }

        return cell == _gapCell && after ? cell : cell - 1;
    }

    // The line of the group's cells `along` past their start; a stack's longer rows walked over, as few as they are.
    private int LineAt(int group, double along)
    {
        if (along < 0)
        {
            return -1;
        }

        if (_extras == null)
        {
            return (int)System.Math.Floor(along / _lineExtent);
        }

        var first = _firstItem[group];
        var end = first + ShownCount(group);
        double extra = 0;
        for (var i = 0; i < _extras.Count && _extras[i].Index < end; i++)
        {
            var index = _extras[i].Index;
            if (index < first)
            {
                continue;
            }

            var cell = CellOf(group, index - first);
            var rowStart = cell * _lineExtent + extra;
            if (along < rowStart)
            {
                break;
            }

            extra += _extras[i].Extra;
            if (along < rowStart + _lineExtent + _extras[i].Extra)
            {
                return cell;
            }
        }

        return (int)System.Math.Floor((along - extra) / _lineExtent);
    }

    // The first group whose lines reach past `along`: a binary search over the starts.
    private int FirstGroupEndingAfter(double along)
    {
        var low = 0;
        var high = _start.Count - 1;
        var found = 0;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (_start[middle] <= along)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    // The last group starting at or before the item: an outer group's header, with no items of its own, starts at the
    // same item as its first inner group and comes before it, so the inner one is found.
    private int GroupOfItem(int index)
    {
        var low = 0;
        var high = _firstItem.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (_firstItem[middle] <= index)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }
}

/// <summary>What of a panel's lines falls in its window: items from <see cref="FirstItem"/> to <see cref="LastItem"/>
/// (none when last is below first), and header places from <see cref="FirstPlace"/> to <see cref="LastPlace"/>.</summary>
internal readonly struct LinesWindow
{
    public LinesWindow(int firstItem, int lastItem, int firstPlace, int lastPlace)
    {
        FirstItem = firstItem;
        LastItem = lastItem;
        FirstPlace = firstPlace;
        LastPlace = lastPlace;
    }

    public int FirstItem { get; }

    public int LastItem { get; }

    public int FirstPlace { get; }

    public int LastPlace { get; }
}

/// <summary>Where in the groups a drop lands: the group of header place <see cref="Place"/> (-1 for items before any
/// header), its items from <see cref="FirstItem"/>, <see cref="ItemCount"/> of them, before its item
/// <see cref="Slot"/> - the count for its end.</summary>
internal readonly struct GroupDropSlot
{
    public GroupDropSlot(int place, int firstItem, int itemCount, int slot)
    {
        Place = place;
        FirstItem = firstItem;
        ItemCount = itemCount;
        Slot = slot;
    }

    public int Place { get; }

    public int FirstItem { get; }

    public int ItemCount { get; }

    public int Slot { get; }
}
