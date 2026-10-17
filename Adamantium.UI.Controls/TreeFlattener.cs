using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Adamantium.UI.Controls;

// Projects a tree into the flat, display-ordered Rows list a virtualizing panel consumes. Expand and collapse splice as
// one range edit; expanded children collections are observed.
internal sealed class TreeFlattener
{
    private readonly Func<object, IEnumerable> _childrenOf;
    private readonly Func<object, bool> _isExpanded;
    private readonly Func<object, bool> _isSelected;
    private readonly Func<object, object> _detailsOf;
    private readonly Dictionary<TreeRow, Subscription> _subs = new();
    private IEnumerable _roots;
    private Subscription _rootsSub;

    // isExpanded/isSelected restore a node's persisted state on (re)build; detailsOf yields an optional details row
    // after a node. Null means none.
    public TreeFlattener(Func<object, IEnumerable> childrenOf, Func<object, bool> isExpanded = null,
        Func<object, bool> isSelected = null, Func<object, object> detailsOf = null)
    {
        _childrenOf = childrenOf;
        _isExpanded = isExpanded ?? (static _ => false);
        _isSelected = isSelected ?? (static _ => false);
        _detailsOf = detailsOf ?? (static _ => null);
        _changes = new CrossThreadChanges<(Subscription, NotifyCollectionChangedEventArgs)>(ApplyChildrenChange);
    }

    private readonly CrossThreadChanges<(Subscription From, NotifyCollectionChangedEventArgs Change)> _changes;

    /// <summary>The visible rows in display order - the virtualized list binds to this.</summary>
    public FlatRowCollection Rows { get; } = [];

    /// <summary>Rebuild the flat list from these roots (all collapsed), observing the roots collection for live changes.</summary>
    public void SetRoots(IEnumerable roots)
    {
        UnsubscribeAllNodes();
        Unsubscribe(null);
        _changes.Clear();
        _changes.TakeThisThread();
        _roots = roots;
        Rows.ResetTo(FollowAndBuild(null, roots, 0));
    }

    /// <summary>Detach every subscription (roots + expanded nodes) - called before this flattener is discarded so its
    /// handlers don't keep the old collections alive or fire into a stale list. The Rows collection is dropped with it.</summary>
    public void Clear()
    {
        UnsubscribeAllNodes();
        Unsubscribe(null);
        _changes.Clear();
        _roots = null;
    }

    public void Toggle(TreeRow row)
    {
        if (row.IsExpanded)
        {
            Collapse(row);
        }
        else
        {
            Expand(row);
        }
    }

    /// <summary>Open a branch: splice its direct children in right after it (collapsed - one level per expand) and start
    /// observing its children collection.</summary>
    public void Expand(TreeRow row)
    {
        if (row.IsExpanded)
        {
            return;
        }

        // Re-evaluate children LIVE rather than trusting the cached flag: a node that was a leaf when its row was built may
        // have gained children since (a drop into it, a lazy load). Refresh the row's flag too, so a former leaf now shows
        // an expander and can open - a stale HasChildren=false would silently swallow the new child.
        var children = _childrenOf(row.Node);
        row.HasChildren = HasAny(children);
        if (!row.HasChildren)
        {
            return;
        }

        row.IsExpanded = true;
        var index = Rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        Rows.InsertMany(AfterDetailsOf(index), FollowAndBuild(row, children, row.Depth + 1));
    }

    // Where a row's children begin: after the row, and after the details panel it may have open - the panel belongs to
    // the row itself, so children spliced in front of it would stand between a record and its own long form.
    private int AfterDetailsOf(int rowIndex)
    {
        var next = rowIndex + 1;
        return next < Rows.Count && Rows[next].IsDetails ? next + 1 : next;
    }

    /// <summary>Close a branch: drop the contiguous run of deeper rows that follows it - its whole VISIBLE subtree -
    /// unsubscribing any expanded descendants along the way, and stop observing its own children.</summary>
    public void Collapse(TreeRow row)
    {
        if (!row.IsExpanded)
        {
            return;
        }

        row.IsExpanded = false;
        Unsubscribe(row);
        var index = Rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        var end = SubtreeEnd(index, row.Depth);
        var from = AfterDetailsOf(index);
        UnsubscribeRange(from, end);
        Rows.RemoveMany(from, end - from);
    }

    // ---- Children observability ---------------------------------------------------------------------------------

    // A node's (or the roots') children changed while it is expanded/visible: reconcile just that node's direct-child
    // region of the flat list. parent == null means the roots collection.
    private void OnChildrenChanged(Subscription from, NotifyCollectionChangedEventArgs e)
    {
        if (!_changes.Defer((from, e))) ApplyChildrenChange((from, e));
    }

    private void ApplyChildrenChange((Subscription From, NotifyCollectionChangedEventArgs Change) change)
    {
        var (from, e) = change;
        var parent = from.Parent;

        // Carried over from another thread after the branch closed, or was read again, or the roots were let go of:
        // what it says is no longer to be applied, or is in the rows already.
        if (!ReferenceEquals(parent == null ? _rootsSub : _subs.GetValueOrDefault(parent), from)) return;

        if (parent != null && Rows.IndexOf(parent) < 0)
        {
            Unsubscribe(parent);   // parent scrolled out of existence
            return;
        }

        var baseDepth = parent == null ? 0 : parent.Depth + 1;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
            {
                var pos = FlatIndexForChild(parent, e.NewStartingIndex);
                var rows = new List<TreeRow>(e.NewItems.Count);
                foreach (var item in e.NewItems)
                {
                    rows.Add(new TreeRow(item, baseDepth, HasChildren(item)) { IsSelected = _isSelected(item) });
                }

                Rows.InsertMany(pos, rows);
                break;
            }
            case NotifyCollectionChangedAction.Remove:
            {
                foreach (var item in e.OldItems)
                {
                    var idx = FindDirectChildRow(parent, item);
                    if (idx < 0)
                    {
                        continue;
                    }

                    var end = SubtreeEnd(idx, Rows[idx].Depth);
                    UnsubscribeRange(idx, end);
                    Rows.RemoveMany(idx, end - idx);
                }

                break;
            }
            default:   // Reset / Replace / Move: rebuild this parent's visible child region.
                RebuildChildrenRegion(parent);
                break;
        }
    }

    private void RebuildChildrenRegion(TreeRow parent)
    {
        if (parent == null)
        {
            UnsubscribeAllNodes();
            Rows.ResetTo(FollowAndBuild(null, _roots, 0));
            return;
        }

        var index = Rows.IndexOf(parent);
        if (index < 0)
        {
            return;
        }

        var end = SubtreeEnd(index, parent.Depth);
        var from = AfterDetailsOf(index);
        UnsubscribeRange(from, end);
        Rows.RemoveMany(from, end - from);
        Rows.InsertMany(from, FollowAndBuild(parent, _childrenOf(parent.Node), parent.Depth + 1));
    }

    // ---- Geometry over the flat list ----------------------------------------------------------------------------

    // First index after `rowIndex` whose Depth is <= `depth` (i.e. one past the row's visible subtree), or Rows.Count.
    private int SubtreeEnd(int rowIndex, int depth)
    {
        var i = rowIndex + 1;

        // The row's OWN details panel comes first and stands at the row's depth, not below it - so the test that
        // follows would stop dead on it and a collapse would take nothing away at all.
        if (i < Rows.Count && Rows[i].IsDetails)
        {
            i++;
        }

        while (i < Rows.Count && Rows[i].Depth > depth)
        {
            i++;
        }

        return i;
    }

    // The flat index at which the child at source position `childIndex` of `parent` (null = roots) belongs - i.e. before
    // the current childIndex-th DIRECT child, skipping the descendant runs of earlier siblings; end of region if past all.
    private int FlatIndexForChild(TreeRow parent, int childIndex)
    {
        var baseDepth = parent == null ? 0 : parent.Depth + 1;
        var i = parent == null ? 0 : Rows.IndexOf(parent) + 1;
        var seen = 0;
        for (; i < Rows.Count; i++)
        {
            // A details panel is not a child of anything - it is the row above it, said at length - and it stands at
            // that row's depth. Read as structure it would be counted as a sibling, and the parent's own panel would
            // read as leaving the parent's subtree on the very first step.
            if (Rows[i].IsDetails)
            {
                continue;
            }

            var depth = Rows[i].Depth;
            if (parent != null && depth <= parent.Depth)
            {
                break;   // left the parent's subtree
            }

            if (depth != baseDepth)
            {
                continue;   // a deeper descendant of an earlier sibling
            }

            if (seen == childIndex)
            {
                return i;
            }

            seen++;
        }

        return i;
    }

    // Flat index of `parent`'s direct child row whose Node equals `item`, or -1.
    private int FindDirectChildRow(TreeRow parent, object item)
    {
        var baseDepth = parent == null ? 0 : parent.Depth + 1;
        var i = parent == null ? 0 : Rows.IndexOf(parent) + 1;
        for (; i < Rows.Count; i++)
        {
            if (Rows[i].IsDetails)
            {
                continue;   // not structure - see FlatIndexForChild
            }

            var depth = Rows[i].Depth;
            if (parent != null && depth <= parent.Depth)
            {
                break;
            }

            if (depth == baseDepth && Equals(Rows[i].Node, item))
            {
                return i;
            }
        }

        return -1;
    }

    private List<TreeRow> FollowAndBuild(TreeRow parent, IEnumerable children, int depth)
    {
        var list = new List<TreeRow>();
        FollowAndAppend(list, parent, children, depth);
        return list;
    }

    // Follows a children collection (null parent = the roots) and reads it in one step under its lock: a change from
    // another thread is either in the rows built or comes after them, and one waiting for the subscription this
    // replaces is not applied. Only the copy is taken under the lock; the rows are built after it is let go.
    private void FollowAndAppend(List<TreeRow> list, TreeRow parent, IEnumerable children, int depth)
    {
        object[] copy;
        int count;
        using (var gate = SourceGate.Hold(children))
        {
            Unsubscribe(parent);
            Subscribe(parent, children);
            copy = gate.Copy(children, out count);
        }

        try
        {
            AppendRows(list, copy == null ? children : new ArraySegment<object>(copy, 0, count), depth);
        }
        finally
        {
            SourceGate.Return(copy);
        }
    }

    // Flatten items at `depth`, RESTORING each node that the view-model still marks expanded (its row is built open and its
    // subtree appended recursively + observed) - so rebuilding the tree (a recreated view over a persisted view-model)
    // comes back with the same branches open, not all collapsed.
    private void AppendRows(List<TreeRow> list, IEnumerable items, int depth)
    {
        if (items == null)
        {
            return;
        }

        foreach (var item in items)
        {
            var hasChildren = HasChildren(item);
            var row = new TreeRow(item, depth, hasChildren) { IsSelected = _isSelected(item) };
            list.Add(row);

            // BEFORE the children and AFTER the row: the panel belongs to the record above it, so it has to sit against
            // that record and not after a subtree that may be a thousand rows long. At the OWNER'S depth, because it is
            // the same record said at length - not a step further into the tree.
            if (_detailsOf(item) is { } details) list.Add(new TreeRow(details, depth, false) { IsDetails = true });

            if (hasChildren && _isExpanded(item))
            {
                row.IsExpanded = true;
                FollowAndAppend(list, row, _childrenOf(item), depth + 1);
            }
        }
    }

    private bool HasChildren(object node) => HasAny(_childrenOf(node));

    private static bool HasAny(IEnumerable children)
    {
        if (children == null)
        {
            return false;
        }

        foreach (var _ in children)
        {
            return true;   // any child?
        }

        return false;
    }

    // ---- Subscriptions ------------------------------------------------------------------------------------------

    private void Subscribe(TreeRow parent, IEnumerable children)
    {
        if (children is not INotifyCollectionChanged incc)
        {
            return;
        }

        var sub = new Subscription(this, parent, incc);
        if (parent == null)
        {
            _rootsSub = sub;
        }
        else
        {
            _subs[parent] = sub;
        }
    }

    private void Unsubscribe(TreeRow parent)
    {
        if (parent == null)
        {
            _rootsSub?.Detach();
            _rootsSub = null;
        }
        else if (_subs.Remove(parent, out var sub))
        {
            sub.Detach();
        }
    }

    // Unsubscribe every expanded row in the flat range [start, end) that is about to be removed.
    private void UnsubscribeRange(int start, int end)
    {
        for (var i = start; i < end && i < Rows.Count; i++)
        {
            if (_subs.Remove(Rows[i], out var sub))
            {
                sub.Detach();
            }
        }
    }

    private void UnsubscribeAllNodes()
    {
        foreach (var sub in _subs.Values)
        {
            sub.Detach();
        }

        _subs.Clear();
    }

    // One following of one children collection: a change carries the subscription it came through, so one that waited
    // while the branch was closed or read again is told from one that still applies.
    private sealed class Subscription
    {
        private readonly TreeFlattener _owner;
        private readonly INotifyCollectionChanged _collection;

        public Subscription(TreeFlattener owner, TreeRow parent, INotifyCollectionChanged collection)
        {
            _owner = owner;
            _collection = collection;
            Parent = parent;
            collection.CollectionChanged += OnChanged;
        }

        public TreeRow Parent { get; }

        public void Detach() => _collection.CollectionChanged -= OnChanged;

        private void OnChanged(object sender, NotifyCollectionChangedEventArgs e) => _owner.OnChildrenChanged(this, e);
    }
}
