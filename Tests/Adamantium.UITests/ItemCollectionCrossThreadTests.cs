using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A SOURCE CHANGED ON ANOTHER THREAD - a game world adding its entities on its own loop - reaches the items on
/// the UI thread, in the order it changed, and never from the world's thread.</summary>
[TestFixture]
public class ItemCollectionCrossThreadTests
{
    private static void OnAnotherThread(Action change) => Task.Run(change).Wait();

    [Test]
    public void AnItemAddedOnAnotherThread_ArrivesOnTheUIThread()
    {
        LoopSignal.Drain();
        var source = new ObservableCollection<string> { "a" };
        var list = new ItemsControl { ItemsSource = source };
        var raisedOn = -1;
        list.Items.CollectionChanged += (_, _) => raisedOn = Environment.CurrentManagedThreadId;

        OnAnotherThread(() => source.Add("b"));

        Assert.That(list.Items, Is.EqualTo(new[] { "a" }), "nothing changes under the UI thread");
        Assert.That(raisedOn, Is.EqualTo(-1));

        LoopSignal.Drain();

        Assert.That(list.Items, Is.EqualTo(new[] { "a", "b" }));
        Assert.That(raisedOn, Is.EqualTo(Environment.CurrentManagedThreadId));
    }

    // A view built off the loop - a deferred tab body, a page made on a worker - is attached to its source there and then
    // lives on the loop: a change from the loop's thread is its own, applied at once, not a frame later.
    [Test]
    public void AListAttachedOffTheLoop_TakesTheLoopsChangesAtOnce()
    {
        var source = new ObservableCollection<string> { "a" };
        ItemsControl list = null;
        OnAnotherThread(() => list = new ItemsControl { ItemsSource = source });
        LoopSignal.Drain();

        source.Add("b");

        Assert.That(list.Items, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void ManyChangesOnAnotherThread_AreAppliedInTheirOrder()
    {
        LoopSignal.Drain();
        var source = new ObservableCollection<int>();
        var list = new ItemsControl { ItemsSource = source };

        OnAnotherThread(() =>
        {
            for (var i = 0; i < 50; i++) source.Add(i);
            for (var i = 0; i < 20; i++) source.RemoveAt(i);
            source.Move(0, 10);
            source[5] = 100;
        });
        LoopSignal.Drain();

        Assert.That(list.Items, Is.EqualTo(source.Cast<object>()));
    }

    // A tree follows its roots through its own flattener, not the item list: the same holds there.
    [Test]
    public void ARootAddedOnAnotherThread_ReachesTheTreeOnTheUIThread()
    {
        LoopSignal.Drain();
        var roots = new ObservableCollection<string> { "a" };
        var flattener = new TreeFlattener(_ => null);
        flattener.SetRoots(roots);

        OnAnotherThread(() => roots.Add("b"));

        Assert.That(flattener.Rows, Has.Count.EqualTo(1), "nothing changes under the UI thread");

        LoopSignal.Drain();

        Assert.That(flattener.Rows, Has.Count.EqualTo(2));
    }

    // A branch closed and opened again while a change to it waits reads its children afresh, the change among them: the
    // change that waited is not applied a second time.
    [Test]
    public void ABranchReadAgainWhileAChangeWaits_ShowsTheChildOnce()
    {
        LoopSignal.Drain();
        var parent = new Node();
        var flattener = new TreeFlattener(node => ((Node)node).Children);
        flattener.SetRoots(new[] { parent });
        parent.Children.Add(new Node());
        var row = flattener.Rows[0];
        flattener.Expand(row);

        OnAnotherThread(() => parent.Children.Add(new Node()));
        flattener.Collapse(row);
        flattener.Expand(row);
        LoopSignal.Drain();

        Assert.That(flattener.Rows, Has.Count.EqualTo(3), "the parent and its two children, each once");
    }

    private sealed class Node
    {
        public ObservableCollection<Node> Children { get; } = [];
    }

    // A source that makes and announces its changes under its SyncRoot - the engine's TrackingCollection - is read
    // under that lock, so it is never seen halfway through a change.
    [Test]
    public void ASourceWithALock_IsReadUnderIt()
    {
        var source = new Locked { "a", "b" };

        var list = new ItemsControl { ItemsSource = source };

        Assert.That(list.Items, Is.EqualTo(new[] { "a", "b" }));
        Assert.That(source.ReadOutsideTheLock, Is.False);
    }

    // Only the copy is taken under it: the rows are built after, so the thread that changes the source never waits on
    // code the tree runs for each item.
    [Test]
    public void ATreeOverASourceWithALock_BuildsItsRowsAfterLettingItGo()
    {
        var source = new Locked { "a", "b" };
        var builtUnderTheLock = false;
        var flattener = new TreeFlattener(_ =>
        {
            builtUnderTheLock |= Monitor.IsEntered(source.SyncRoot);
            return null;
        });

        flattener.SetRoots(source);

        Assert.That(flattener.Rows, Has.Count.EqualTo(2));
        Assert.That(source.ReadOutsideTheLock, Is.False, "the copy is taken under the lock");
        Assert.That(builtUnderTheLock, Is.False);
    }

    private sealed class Locked : ICollection, INotifyCollectionChanged
    {
        private readonly List<object> _items = [];

        public bool ReadOutsideTheLock { get; private set; }

        public object SyncRoot { get; } = new();

        public bool IsSynchronized => true;

        public int Count => _items.Count;

        public void Add(object item)
        {
            lock (SyncRoot)
            {
                _items.Add(item);
                CollectionChanged?.Invoke(this,
                    new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, _items.Count - 1));
            }
        }

        public IEnumerator GetEnumerator()
        {
            ReadOutsideTheLock |= !Monitor.IsEntered(SyncRoot);
            return _items.GetEnumerator();
        }

        public void CopyTo(Array array, int index)
        {
            ReadOutsideTheLock |= !Monitor.IsEntered(SyncRoot);
            ((ICollection)_items).CopyTo(array, index);
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;
    }

    // A change that came in a different order than the items show - here made by hand - reads the source again
    // instead of putting the list out of step with it.
    [Test]
    public void AChangeThatDoesNotFit_ReadsTheSourceAgain()
    {
        var source = new Raising { "a", "b" };
        var list = new ItemsControl { ItemsSource = source };
        NotifyCollectionChangedAction? seen = null;
        list.Items.CollectionChanged += (_, e) => seen = e.Action;

        source.Add("c");
        source.RaiseRemove("x", 0);

        Assert.That(seen, Is.EqualTo(NotifyCollectionChangedAction.Reset));
        Assert.That(list.Items, Is.EqualTo(new[] { "a", "b", "c" }));
    }

    private sealed class Raising : Collection<string>, INotifyCollectionChanged
    {
        public void RaiseRemove(string item, int index) =>
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));

        public event NotifyCollectionChangedEventHandler CollectionChanged;
    }
}
