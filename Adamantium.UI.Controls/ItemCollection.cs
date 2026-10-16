using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Adamantium.UI.Controls;

/// <summary>
/// The effective item list of an <see cref="ItemsControl"/>: either items authored directly in markup
/// (<c>&lt;ItemsControl&gt;&lt;Button/&gt;…</c>) or a view over an assigned <c>ItemsSource</c>. While a source is set the
/// collection is read-only (mutate the source instead), mirroring WPF. Raises <see cref="INotifyCollectionChanged"/>
/// so the control regenerates containers. A source that raises its own change notifications is mirrored live - also
/// one changed on another thread, a game world's entities: its changes are carried over to the UI thread in order.
/// </summary>
public sealed class ItemCollection : IList<object>, IReadOnlyList<object>, INotifyCollectionChanged
{
    private readonly List<object> _direct = [];
    private IEnumerable _source;
    private List<object> _sourceSnapshot;

    private readonly CrossThreadChanges<NotifyCollectionChangedEventArgs> _changes;

    public ItemCollection()
    {
        _changes = new CrossThreadChanges<NotifyCollectionChangedEventArgs>(Apply);
    }

    public event NotifyCollectionChangedEventHandler CollectionChanged;

    internal bool HasSource => _sourceSnapshot != null;

    private IList<object> Active => _sourceSnapshot ?? (IList<object>)_direct;

    public int Count => Active.Count;

    public bool IsReadOnly => HasSource;

    public object this[int index]
    {
        get => Active[index];
        set
        {
            ThrowIfSourced();
            var old = _direct[index];
            _direct[index] = value;
            Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, old, index));
        }
    }

    /// <summary>Switches the collection to view <paramref name="source"/> (null clears it, returning to direct items).
    /// If the source raises <see cref="INotifyCollectionChanged"/> its changes are mirrored live and forwarded.</summary>
    internal void SetSource(IEnumerable source)
    {
        LetGoOfSource();
        _changes.TakeThisThread();
        _source = source;

        // Read and followed in one step under the source's lock: a change from another thread is either in what is read
        // or arrives after it.
        using (SourceGate.Hold(source))
        {
            _sourceSnapshot = source?.Cast<object>().ToList();

            if (source is INotifyCollectionChanged observable)
                observable.CollectionChanged += OnSourceCollectionChanged;
        }

        Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    internal void LetGoOfSource()
    {
        // Under the source's lock, so a change from another thread being announced now is waiting already and dropped.
        using (SourceGate.Hold(_source))
        {
            if (_source is INotifyCollectionChanged observable)
            {
                observable.CollectionChanged -= OnSourceCollectionChanged;
            }

            _changes.Clear();
        }

        _source = null;
        _sourceSnapshot = null;
    }

    private void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_changes.Defer(e)) Apply(e);
    }

    // Mirror the source's change into the snapshot (so indexing stays correct), then forward the SAME args so the
    // ItemsControl can update containers incrementally with the source's own indices.
    private void Apply(NotifyCollectionChangedEventArgs e)
    {
        if (_sourceSnapshot == null) return;

        // A change that does not fit the snapshot - a source that announced it wrongly - is answered by reading the
        // source again.
        if (!Fits(e)) e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (var i = 0; i < e.NewItems.Count; i++)
                    _sourceSnapshot.Insert(e.NewStartingIndex + i, e.NewItems[i]);
                break;
            case NotifyCollectionChangedAction.Remove:
                _sourceSnapshot.RemoveRange(e.OldStartingIndex, e.OldItems.Count);
                break;
            case NotifyCollectionChangedAction.Replace:
                for (var i = 0; i < e.NewItems.Count; i++)
                    _sourceSnapshot[e.OldStartingIndex + i] = e.NewItems[i];
                break;
            // A MOVE is carried, not flattened into a rebuild. Live sorting reports an item changing places as exactly
            // this, and answering it with Reset would throw away every realized container on the screen to relocate one
            // row - which is the difference between a row sliding to its new place and the list blinking.
            case NotifyCollectionChangedAction.Move:
                var moved = _sourceSnapshot[e.OldStartingIndex];
                _sourceSnapshot.RemoveAt(e.OldStartingIndex);
                _sourceSnapshot.Insert(e.NewStartingIndex, moved);
                break;
            case NotifyCollectionChangedAction.Reset:
                // What waits from another thread is in what is read here.
                using (SourceGate.Hold(_source))
                {
                    _changes.Clear();
                    _sourceSnapshot = _source?.Cast<object>().ToList() ?? [];
                }

                Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                return;
        }

        Raise(e);
    }

    private bool Fits(NotifyCollectionChangedEventArgs e)
    {
        var count = _sourceSnapshot.Count;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                return e.NewStartingIndex >= 0 && e.NewStartingIndex <= count;
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                return Holds(e.OldStartingIndex, e.OldItems);
            case NotifyCollectionChangedAction.Move:
                return Holds(e.OldStartingIndex, e.OldItems) && e.NewStartingIndex >= 0 && e.NewStartingIndex < count;
            default:
                return true;
        }
    }

    private bool Holds(int index, IList items)
    {
        if (index < 0 || items == null || index + items.Count > _sourceSnapshot.Count) return false;

        for (var i = 0; i < items.Count; i++)
        {
            if (!Equals(_sourceSnapshot[index + i], items[i])) return false;
        }

        return true;
    }

    public void Add(object item)
    {
        ThrowIfSourced();
        _direct.Add(item);
        Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, _direct.Count - 1));
    }

    public void Insert(int index, object item)
    {
        ThrowIfSourced();
        _direct.Insert(index, item);
        Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    public bool Remove(object item)
    {
        ThrowIfSourced();
        var index = _direct.IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index)
    {
        ThrowIfSourced();
        var old = _direct[index];
        _direct.RemoveAt(index);
        Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, old, index));
    }

    public void Clear()
    {
        ThrowIfSourced();
        _direct.Clear();
        Raise(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public bool Contains(object item) => Active.Contains(item);

    public int IndexOf(object item) => Active.IndexOf(item);

    public void CopyTo(object[] array, int arrayIndex) => Active.CopyTo(array, arrayIndex);

    public IEnumerator<object> GetEnumerator() => Active.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Raise(NotifyCollectionChangedEventArgs e) => CollectionChanged?.Invoke(this, e);

    private void ThrowIfSourced()
    {
        if (HasSource)
            throw new InvalidOperationException("Items is read-only while ItemsSource is set; modify the source collection instead.");
    }
}
