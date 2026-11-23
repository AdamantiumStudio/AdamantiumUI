using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.Collections;

namespace Adamantium.UI.Core.Collections;

/// <summary>A live filtered, sorted and paged view over a collection, always in that order, and grouped by its
/// <see cref="GroupDescriptions"/>: grouped, its items come group by group. Item changes are reported as narrow
/// Add/Remove at view indices; Reset for a new filter, ordering, grouping or source - and, grouped, for a change that
/// brings one group before another or with more than one level of groups.</summary>
public class CollectionView : IEnumerable, IReadOnlyList<object>, INotifyCollectionChanged, IPagedSource
{
    private IReadOnlyList<CollectionViewGroup> _groups = [];
    private bool _groupsStale;
    private readonly List<object> _snapshot = [];
    private readonly List<object> _view = [];
    private readonly List<int> _sourceIndexOfView = [];
    private readonly List<object> _outerNameOfView = [];   // grouped: each shown item's outermost group name
    private readonly Dictionary<object, int> _subscribed = new();
    private readonly List<object> _page = [];
    private int _pageSize;
    private int _pageIndex;
    private IEnumerable _source;
    private Predicate<object> _filter;
    private IComparer _customSort;
    private bool _isLiveFiltering;
    private bool _isLiveSorting;

    /// <summary>Creates a view over <paramref name="source"/>; null is a valid (empty) source.</summary>
    public CollectionView(IEnumerable source = null)
    {
        SortDescriptions = new SortDescriptionCollection();
        SortDescriptions.CollectionChanged += (_, _) => Rebuild();
        GroupDescriptions = [];
        GroupDescriptions.CollectionChanged += (_, _) => Rebuild();
        LiveFilteringProperties = [];
        LiveSortingProperties = [];
        Source = source;
    }

    /// <inheritdoc/>
    public event NotifyCollectionChangedEventHandler CollectionChanged;

    /// <summary>The collection being viewed. Its own change notifications are mirrored while it is set, so the view
    /// stays live; assigning a new one re-reads it and raises a Reset.</summary>
    public IEnumerable Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value)) return;

            if (_source is INotifyCollectionChanged oldObservable)
                oldObservable.CollectionChanged -= OnSourceCollectionChanged;

            _source = value;

            if (_source is INotifyCollectionChanged observable)
                observable.CollectionChanged += OnSourceCollectionChanged;

            Rebuild();
        }
    }

    internal void LetGoOfSource()
    {
        if (_source is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged -= OnSourceCollectionChanged;
        }

        foreach (var item in _subscribed.Keys)
        {
            if (item is INotifyPropertyChanged record)
            {
                record.PropertyChanged -= OnItemPropertyChanged;
            }
        }

        _subscribed.Clear();
        _source = null;
    }

    /// <summary>Which items are shown; null shows all. Setting it raises a Reset; item changes are tracked by
    /// <see cref="IsLiveFiltering"/>.</summary>
    public Predicate<object> Filter
    {
        get => _filter;
        set
        {
            if (_filter == value) return;
            _filter = value;
            Rebuild();
        }
    }

    /// <summary>The ordering levels, in priority order. Empty leaves the source's own order alone.
    /// <para>Ignored while <see cref="CustomSort"/> is set - a comparer is the whole answer, and honoring both would
    /// mean guessing which one the author meant.</para></summary>
    public SortDescriptionCollection SortDescriptions { get; }

    /// <summary>An ordering the descriptions cannot express (case rules, a computed key, a domain order). Takes
    /// precedence over <see cref="SortDescriptions"/>.
    /// <para>The cost of the escape hatch: a comparer is opaque, so live sorting cannot work out which properties to
    /// listen to and they have to be named in <see cref="LiveSortingProperties"/>.</para></summary>
    public IComparer CustomSort
    {
        get => _customSort;
        set
        {
            if (ReferenceEquals(_customSort, value)) return;
            _customSort = value;
            Rebuild();
        }
    }

    /// <summary>True while anything is ordering the view - which is also when its order stops matching the source's.</summary>
    public bool IsSorted => _customSort != null || SortDescriptions.Count > 0;

    /// <summary>The levels the shown items are grouped by, the first the outermost; empty groups nothing. Grouped, the
    /// view's items come group by group - each group where its first item comes after the filter and the ordering, the
    /// ordering kept inside it - so a list can show a group's header and then its items.</summary>
    public TrackingCollection<GroupDescription> GroupDescriptions { get; }

    /// <summary>The shown items in groups, by the first of <see cref="GroupDescriptions"/>, each in the order its first
    /// item comes in the view - sorting by the grouped property first orders the groups too; empty while nothing groups
    /// them. A new list after each change of what is shown, told by <see cref="PropertyChanged"/> and made when it is
    /// next read - up to date for a handler of <see cref="CollectionChanged"/> too.</summary>
    public IReadOnlyList<CollectionViewGroup> Groups
    {
        get
        {
            if (_groupsStale)
            {
                _groupsStale = false;
                var shown = Shown();
                // The outer names as the items were put in order: an item whose name changed since, with no live
                // grouping, stays in the group it is shown in until the view is made again - not a second one of a name.
                _groups = IsGrouped
                    ? GroupsOf(shown, 0, _outerNameOfView.Count == shown.Count ? _outerNameOfView : null)
                    : [];
            }

            return _groups;
        }
    }

    /// <summary>Whether <see cref="GroupDescriptions"/> group the items.</summary>
    public bool IsGrouped => GroupDescriptions.Count > 0;

    /// <summary>Re-evaluates the filter when an item changes. Off by default, since it subscribes to every item.</summary>
    public bool IsLiveFiltering
    {
        get => _isLiveFiltering;
        set
        {
            if (_isLiveFiltering == value) return;
            _isLiveFiltering = value;
            ResubscribeItems();
        }
    }

    /// <summary>Re-place an item when the property it is ordered by changes, so it moves to where it now belongs
    /// instead of staying put until the next rebuild. Off by default, for the same reason as <see cref="IsLiveFiltering"/>.</summary>
    public bool IsLiveSorting
    {
        get => _isLiveSorting;
        set
        {
            if (_isLiveSorting == value) return;
            _isLiveSorting = value;
            ResubscribeItems();
        }
    }

    /// <summary>Which property changes the FILTER cares about. Empty means every change re-evaluates the predicate,
    /// which on a long list is the expensive answer - name them.</summary>
    public TrackingCollection<string> LiveFilteringProperties { get; }

    /// <summary>Properties whose change re-places an item; empty means those in <see cref="SortDescriptions"/>, or every
    /// change with a <see cref="CustomSort"/>.</summary>
    public TrackingCollection<string> LiveSortingProperties { get; }

    /// <summary>How many items a page holds; 0 - the default - shows the whole shaped set and pages nothing.
    /// <para>Changing it keeps the FIRST ITEM of the current page in view rather than dropping to the beginning: the
    /// page is renumbered around where the reader already is.</para></summary>
    public int PageSize
    {
        get => _pageSize;
        set
        {
            var size = Math.Max(0, value);
            if (_pageSize == size) return;

            var firstItem = _pageSize > 0 ? _pageIndex * _pageSize : 0;
            _pageSize = size;
            _pageIndex = size > 0 ? firstItem / size : 0;
            ClampPage();
            RebuildPage();
        }
    }

    /// <summary>Which page is shown, counted from zero. Out-of-range values are clamped to what exists.</summary>
    public int PageIndex
    {
        get => _pageIndex;
        set
        {
            var index = Math.Max(0, value);
            if (_pageIndex == index) return;
            _pageIndex = index;
            ClampPage();
            RebuildPage();
        }
    }

    /// <summary>How many pages the shaped set makes; 0 when nothing is shown, 1 when paging is off.</summary>
    public int PageCount => IsPaged ? (_view.Count + _pageSize - 1) / _pageSize : _view.Count > 0 ? 1 : 0;

    /// <summary>How many items there are to page THROUGH - the shaped count, which is what the numbers on a pager are
    /// about. Not the source's count: a filtered-out item is not on any page.</summary>
    public int TotalItemCount => _view.Count;

    /// <summary>True while <see cref="PageSize"/> is cutting the shaped set into pages.</summary>
    public bool IsPaged => _pageSize > 0;

    /// <summary>How many items are shown right now: a page's worth while paging, everything shaped otherwise.</summary>
    public int Count => IsPaged ? _page.Count : _view.Count;

    /// <inheritdoc/>
    public object this[int index] => IsPaged ? _page[index] : _view[index];

    /// <summary>How many items the SOURCE holds, before any of the stages. What a pager needs to say how much there is
    /// in total, as opposed to how much of it is shown.</summary>
    public int SourceCount => _snapshot.Count;

    /// <inheritdoc/>
    public IEnumerator<object> GetEnumerator() => IsPaged ? _page.GetEnumerator() : _view.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Filters and orders the source again - for a filter or an ordering that reads something outside the items,
    /// such as the text of a search box. Raises a Reset.</summary>
    public void Refresh() => Rebuild();

    private bool Passes(object item) => _filter == null || _filter(item);

    private void Rebuild()
    {
        _snapshot.Clear();
        _view.Clear();
        _sourceIndexOfView.Clear();

        if (_source != null)
        {
            foreach (var item in _source) _snapshot.Add(item);
        }

        ResubscribeItems();

        for (var i = 0; i < _snapshot.Count; i++)
        {
            if (!Passes(_snapshot[i])) continue;
            _view.Add(_snapshot[i]);
            _sourceIndexOfView.Add(i);
        }

        if (IsSorted) SortView();

        ClampPage();
        RebuildPage();
    }

    // Group by group, each where its first item comes, the levels below within; the order inside a group kept. Made on
    // every rebuild of the page - which is told as a Reset - so a name an item changed without a word is taken then too.
    private void PutInGroupOrder()
    {
        var positions = new List<int>(_view.Count);
        for (var i = 0; i < _view.Count; i++)
            positions.Add(i);

        var ordered = new List<int>(_view.Count);
        AddInGroupOrder(positions, 0, ordered);

        var items = new List<object>(_view.Count);
        var indexes = new List<int>(_view.Count);
        foreach (var position in ordered)
        {
            items.Add(_view[position]);
            indexes.Add(_sourceIndexOfView[position]);
        }

        _view.Clear();
        _view.AddRange(items);
        _sourceIndexOfView.Clear();
        _sourceIndexOfView.AddRange(indexes);

        _outerNameOfView.Clear();
        var outer = GroupDescriptions[0];
        foreach (var item in _view)
            _outerNameOfView.Add(outer.GroupNameOf(item));
    }

    // Grouped by one level, the view goes group by group, the groups in the order of their first items: an item joins
    // its group where the ordering puts it, a new group goes before the first group whose first item comes after it.
    // -1 when the item would bring its group before the one ahead of it - the order of the groups changes - or with more
    // levels: the view is then made again.
    private int GroupedInsertionPoint(object item, int sourceIndex)
    {
        if (GroupDescriptions.Count > 1)
            return -1;

        var name = GroupDescriptions[0].GroupNameOf(item);
        var start = -1;
        var end = -1;
        for (var i = 0; i < _outerNameOfView.Count; i++)
        {
            if (Equals(_outerNameOfView[i], name))
            {
                if (start < 0)
                    start = i;
                end = i + 1;
            }
            else if (start >= 0)
            {
                break;
            }
        }

        if (start < 0)
        {
            for (var i = 0; i < _view.Count; i++)
            {
                if (StartsGroup(i) && Order(item, sourceIndex, i) < 0)
                    return i;
            }

            return _view.Count;
        }

        var low = start;
        var high = end;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Order(item, sourceIndex, middle) > 0)
                low = middle + 1;
            else
                high = middle;
        }

        if (low == start && start > 0 && Order(item, sourceIndex, FirstOfGroupAt(start - 1)) < 0)
            return -1;

        return low;
    }

    // Whether taking the item at `position` keeps the groups in the order of their first items: it does unless it is a
    // group's first and the group's next item comes after the next group's first.
    private bool RemovalKeepsGroupOrder(int position)
    {
        if (GroupDescriptions.Count > 1)
            return false;

        if (!StartsGroup(position) || position + 1 >= _view.Count || StartsGroup(position + 1))
            return true;

        var nextGroup = position + 1;
        while (nextGroup < _view.Count && !StartsGroup(nextGroup))
            nextGroup++;

        return nextGroup >= _view.Count || Order(_view[nextGroup], _sourceIndexOfView[nextGroup], position + 1) > 0;
    }

    private bool StartsGroup(int position) =>
        position == 0 || !Equals(_outerNameOfView[position], _outerNameOfView[position - 1]);

    private int FirstOfGroupAt(int position)
    {
        while (position > 0 && !StartsGroup(position))
            position--;

        return position;
    }

    // Where `item` from `sourceIndex` comes against the view's item at `position`: by the ordering, then by the source.
    private int Order(object item, int sourceIndex, int position)
    {
        var byKey = IsSorted ? Compare(item, _view[position]) : 0;
        return byKey != 0 ? byKey : sourceIndex.CompareTo(_sourceIndexOfView[position]);
    }

    private void AddInGroupOrder(List<int> positions, int level, List<int> ordered)
    {
        if (level == GroupDescriptions.Count)
        {
            ordered.AddRange(positions);
            return;
        }

        var description = GroupDescriptions[level];
        var members = new List<List<int>>();
        var indexOfName = new Dictionary<object, int>();
        var indexOfNull = -1;
        foreach (var position in positions)
        {
            var name = description.GroupNameOf(_view[position]);
            var index = name == null ? indexOfNull : indexOfName.GetValueOrDefault(name, -1);
            if (index < 0)
            {
                members.Add([]);
                index = members.Count - 1;
                if (name == null)
                    indexOfNull = index;
                else
                    indexOfName[name] = index;
            }

            members[index].Add(position);
        }

        foreach (var group in members)
            AddInGroupOrder(group, level + 1, ordered);
    }

    private void SortView()
    {
        var order = Enumerable.Range(0, _view.Count).ToList();
        order.Sort((a, b) =>
        {
            var byKey = Compare(_view[a], _view[b]);
            return byKey != 0 ? byKey : _sourceIndexOfView[a].CompareTo(_sourceIndexOfView[b]);
        });

        var items = order.Select(i => _view[i]).ToList();
        var indexes = order.Select(i => _sourceIndexOfView[i]).ToList();
        _view.Clear();
        _view.AddRange(items);
        _sourceIndexOfView.Clear();
        _sourceIndexOfView.AddRange(indexes);
    }

    private int Compare(object left, object right)
    {
        if (_customSort != null) return _customSort.Compare(left, right);

        foreach (var description in SortDescriptions)
        {
            var result = Comparer<object>.Default.Compare(
                ValueOf(left, description.PropertyName), ValueOf(right, description.PropertyName));
            if (result == 0) continue;
            return description.Direction == SortDirection.Descending ? -result : result;
        }

        return 0;
    }

    private static object ValueOf(object item, string propertyName)
    {
        return Adamantium.MVVM.PropertyAccessors.GetValue(item, propertyName);
    }

    private void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // Grouped by more than one level, the view is made again; by one, an item joins its group (GroupedInsertionPoint).
        if (GroupDescriptions.Count > 1)
        {
            Rebuild();
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewStartingIndex >= 0:
                SourceInserted(e.NewStartingIndex, e.NewItems);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex >= 0:
                SourceRemoved(e.OldStartingIndex, e.OldItems.Count);
                break;
            case NotifyCollectionChangedAction.Replace when e.OldStartingIndex >= 0:
                // Made again on the way - from the source as it is now, the new items in it - nothing is left to insert.
                if (!SourceRemoved(e.OldStartingIndex, e.OldItems.Count))
                    SourceInserted(e.OldStartingIndex, e.NewItems);
                break;
            default:
                Rebuild();
                break;
        }
    }

    private void SourceInserted(int sourceIndex, IList items)
    {
        ShiftSourceIndexes(sourceIndex, items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var at = sourceIndex + i;
            _snapshot.Insert(at, item);
            Subscribe(item);
            if (!Passes(item)) continue;

            var viewIndex = InsertionPointFor(item, at);
            if (viewIndex < 0)
            {
                Rebuild();   // the groups change order: made again, from the source as it is now
                return;
            }

            InsertShown(viewIndex, item, at);
            ShapedInserted(viewIndex, item);
        }
    }

    private void InsertShown(int position, object item, int sourceIndex)
    {
        _view.Insert(position, item);
        _sourceIndexOfView.Insert(position, sourceIndex);
        if (IsGrouped)
            _outerNameOfView.Insert(position, GroupDescriptions[0].GroupNameOf(item));
    }

    private void RemoveShown(int position)
    {
        _view.RemoveAt(position);
        _sourceIndexOfView.RemoveAt(position);
        if (IsGrouped)
            _outerNameOfView.RemoveAt(position);
    }

    // True when the view was made again from the source instead.
    private bool SourceRemoved(int sourceIndex, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            var at = sourceIndex + i;
            var found = ViewPositionOfSource(at);
            if (found >= 0)
            {
                if (IsGrouped && !RemovalKeepsGroupOrder(found))
                {
                    Rebuild();   // the groups change order: made again, from the source as it is now
                    return true;
                }

                var item = _view[found];
                RemoveShown(found);
                ShapedRemoved(found, item);
            }

            Unsubscribe(_snapshot[at]);
            _snapshot.RemoveAt(at);
        }

        ShiftSourceIndexes(sourceIndex, -count);
        return false;
    }

    private void ShiftSourceIndexes(int fromSourceIndex, int delta)
    {
        for (var i = _sourceIndexOfView.Count - 1; i >= 0; i--)
        {
            if (_sourceIndexOfView[i] < fromSourceIndex)
            {
                if (IsReordered) continue;
                break;
            }

            _sourceIndexOfView[i] += delta;
        }
    }

    // Sorted or grouped, the view's order is not the source's: a source index is looked for, not searched in halves.
    private bool IsReordered => IsSorted || IsGrouped;

    private int ViewPositionOfSource(int sourceIndex)
    {
        if (!IsReordered) return _sourceIndexOfView.BinarySearch(sourceIndex);

        for (var i = 0; i < _sourceIndexOfView.Count; i++)
        {
            if (_sourceIndexOfView[i] == sourceIndex) return i;
        }

        return -1;
    }

    // Where a new shown item goes; -1 when the view has to be made again for it (grouped: the groups change order).
    private int InsertionPointFor(object item, int sourceIndex)
    {
        if (IsGrouped)
            return GroupedInsertionPoint(item, sourceIndex);

        if (!IsSorted)
        {
            var found = _sourceIndexOfView.BinarySearch(sourceIndex);
            return found >= 0 ? found : ~found;
        }

        var low = 0;
        var high = _view.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            var order = Compare(_view[mid], item);
            if (order == 0) order = _sourceIndexOfView[mid].CompareTo(sourceIndex);
            if (order <= 0) low = mid + 1;
            else high = mid;
        }

        return low;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler PropertyChanged;

    /// <inheritdoc/>
    public event EventHandler<PageChangingEventArgs> PageChanging;

    /// <inheritdoc/>
    public event EventHandler<PageChangedEventArgs> PageChanged;

    /// <summary>Always true for an in-memory view: turning a page is a slice, and there is nothing to wait for.</summary>
    public bool CanChangePage => true;

    /// <summary>Always false for an in-memory view - see <see cref="CanChangePage"/>.</summary>
    public bool IsPageChanging => false;

    int? IPagedSource.TotalItemCount => TotalItemCount;

    int? IPagedSource.PageCount => PageCount;

    /// <summary>Turns to <paramref name="pageIndex"/>, honoring a <see cref="PageChanging"/> veto. Completed by the
    /// time it returns: the same contract as a server source, so a pager has ONE path rather than one per kind.</summary>
    public Task<bool> MoveToPageAsync(int pageIndex)
    {
        if (pageIndex < 0 || (IsPaged && pageIndex >= PageCount && PageCount > 0)) return Task.FromResult(false);

        var changing = new PageChangingEventArgs(pageIndex);
        PageChanging?.Invoke(this, changing);
        if (changing.Cancel) return Task.FromResult(false);

        PageIndex = pageIndex;
        PageChanged?.Invoke(this, new PageChangedEventArgs(_pageIndex));
        return Task.FromResult(true);
    }

    private int PageStart => _pageIndex * _pageSize;

    private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    private void ClampPage()
    {
        if (!IsPaged) { _pageIndex = 0; return; }
        var last = Math.Max(0, PageCount - 1);
        if (_pageIndex > last) _pageIndex = last;
    }

    private void RebuildPage()
    {
        if (IsGrouped)
            PutInGroupOrder();
        else
            _outerNameOfView.Clear();

        if (!IsPaged)
        {
            _page.Clear();
            Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            return;
        }

        _page.Clear();
        for (var i = PageStart; i < _view.Count && _page.Count < _pageSize; i++) _page.Add(_view[i]);
        Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        Notify(nameof(PageIndex));
        Notify(nameof(PageCount));
        Notify(nameof(TotalItemCount));
        Notify(nameof(Count));
    }

    private void ShapedInserted(int shapedIndex, object item)
    {
        if (!IsPaged)
        {
            Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, shapedIndex));
            return;
        }

        if (shapedIndex >= PageStart + _pageSize) return;

        var at = Math.Max(0, shapedIndex - PageStart);
        var entering = _view[PageStart + at];
        _page.Insert(at, entering);
        Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, entering, at));

        if (_page.Count <= _pageSize) return;

        var pushed = _page[_pageSize];
        _page.RemoveAt(_pageSize);
        Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, pushed, _pageSize));
    }

    private void ShapedRemoved(int shapedIndex, object item)
    {
        if (!IsPaged)
        {
            Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, shapedIndex));
            return;
        }

        if (PageStart >= _view.Count && _view.Count > 0)
        {
            ClampPage();
            RebuildPage();
            return;
        }

        if (shapedIndex >= PageStart + _pageSize) return;

        var at = Math.Max(0, shapedIndex - PageStart);
        if (at >= _page.Count) return;

        var leaving = _page[at];
        _page.RemoveAt(at);
        Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, leaving, at));

        var next = PageStart + _page.Count;
        if (next >= _view.Count) return;

        _page.Add(_view[next]);
        Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, _view[next], _page.Count - 1));
    }

    private void ShapedMoved(int from, int to, object item)
    {
        if (!IsPaged)
        {
            Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, to, from));
            return;
        }

        var end = PageStart + _pageSize;
        if (from >= PageStart && from < end && to >= PageStart && to < end)
        {
            _page.RemoveAt(from - PageStart);
            _page.Insert(to - PageStart, item);
            Raise(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Move, item, to - PageStart, from - PageStart));
            return;
        }

        RebuildPage();
    }

    private bool IsLiveShaping => _isLiveFiltering || _isLiveSorting;

    private void ResubscribeItems()
    {
        foreach (var item in _subscribed.Keys)
        {
            if (item is INotifyPropertyChanged observable) observable.PropertyChanged -= OnItemPropertyChanged;
        }

        _subscribed.Clear();
        if (!IsLiveShaping) return;

        foreach (var item in _snapshot) Subscribe(item);
    }

    private void Subscribe(object item)
    {
        if (!IsLiveShaping || item is not INotifyPropertyChanged observable) return;

        if (_subscribed.TryGetValue(item, out var count))
        {
            _subscribed[item] = count + 1;
            return;
        }

        _subscribed[item] = 1;
        observable.PropertyChanged += OnItemPropertyChanged;
    }

    private void Unsubscribe(object item)
    {
        if (item is not INotifyPropertyChanged observable) return;
        if (!_subscribed.TryGetValue(item, out var count)) return;

        if (count > 1)
        {
            _subscribed[item] = count - 1;
            return;
        }

        _subscribed.Remove(item);
        observable.PropertyChanged -= OnItemPropertyChanged;
    }

    private bool Affects(ICollection<string> names, string changed) =>
        string.IsNullOrEmpty(changed) || names.Count == 0 || names.Contains(changed);

    private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var refiltered = _isLiveFiltering && Affects(LiveFilteringProperties, e.PropertyName);
        var reordered = _isLiveSorting && IsSorted && Affects(SortingPropertiesFor(), e.PropertyName);
        if (!refiltered && !reordered) return;

        var sourceIndex = _snapshot.IndexOf(sender);
        if (sourceIndex < 0) return;

        var position = ViewPositionOfSource(sourceIndex);
        var shown = position >= 0;
        var passes = Passes(sender);

        // Grouped, an item leaving its place can bring a group forward or back: then the view is made again.
        if (shown && (!passes || reordered) && IsGrouped && !RemovalKeepsGroupOrder(position))
        {
            Rebuild();
            return;
        }

        if (shown && !passes)
        {
            RemoveShown(position);
            ShapedRemoved(position, sender);
            return;
        }

        if (!shown && passes)
        {
            var at = InsertionPointFor(sender, sourceIndex);
            if (at < 0)
            {
                Rebuild();
                return;
            }

            InsertShown(at, sender, sourceIndex);
            ShapedInserted(at, sender);
            return;
        }

        if (!shown || !reordered) return;

        RemoveShown(position);
        var moved = InsertionPointFor(sender, sourceIndex);
        if (moved < 0)
        {
            Rebuild();
            return;
        }

        InsertShown(moved, sender, sourceIndex);
        if (moved != position) ShapedMoved(position, moved, sender);
    }

    private ICollection<string> SortingPropertiesFor()
    {
        if (LiveSortingProperties.Count > 0 || _customSort != null) return LiveSortingProperties;

        var names = new List<string>(SortDescriptions.Count);
        foreach (var description in SortDescriptions) names.Add(description.PropertyName);
        return names;
    }

    private void Raise(NotifyCollectionChangedEventArgs e)
    {
        var regroups = IsGrouped || _groups.Count > 0;
        if (regroups)
            _groupsStale = true;

        CollectionChanged?.Invoke(this, e);
        if (regroups)
            Notify(nameof(Groups));
    }

    private List<object> Shown()
    {
        var shown = new List<object>(Count);
        foreach (var item in this)
            shown.Add(item);

        return shown;
    }

    // The items in groups by the level's description, the levels below within. The view is in group order already, so a
    // group is a run of items of one name.
    private List<CollectionViewGroup> GroupsOf(List<object> items, int level, IReadOnlyList<object> namesInOrder = null)
    {
        var description = GroupDescriptions[level];
        var names = new List<object>();
        var members = new List<List<object>>();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var name = namesInOrder != null ? namesInOrder[index] : description.GroupNameOf(item);
            if (names.Count == 0 || !Equals(names[^1], name))
            {
                names.Add(name);
                members.Add([]);
            }

            members[^1].Add(item);
        }

        var isBottom = level == GroupDescriptions.Count - 1;
        var groups = new List<CollectionViewGroup>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            IReadOnlyList<object> inside = isBottom ? members[i] : GroupsOf(members[i], level + 1);
            groups.Add(new CollectionViewGroup(names[i], inside, members[i].Count, isBottom));
        }

        return groups;
    }
}
