using System.Collections.ObjectModel;
using System.ComponentModel;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

// ListBox single-selection + the recycling-correctness that matters for a virtualized list: selection lives on the
// ListBox (by item), and is reflected onto whichever container currently hosts that item - even a recycled one rebound
// to it on scroll. Containers are realized directly via the generator (no GPU/layout needed for these).
public class ListBoxTests
{
    private static ListBox MakeListBox(params string[] items) => new() { ItemsSource = items };

    [Test]
    public void GeneratedContainer_IsListBoxItem()
    {
        var lb = MakeListBox("a");
        Assert.That(lb.ItemContainerGenerator.Realize(0), Is.InstanceOf<ListBoxItem>());
    }

    [Test]
    public void SelectedIndex_SetsItem_AndReflectsOntoContainers()
    {
        var lb = MakeListBox("a", "b", "c");
        var g = lb.ItemContainerGenerator;
        var c0 = (ListBoxItem)g.Realize(0);
        var c1 = (ListBoxItem)g.Realize(1);
        var c2 = (ListBoxItem)g.Realize(2);

        lb.SelectedIndex = 1;

        Assert.Multiple(() =>
        {
            Assert.That(lb.SelectedItem, Is.EqualTo("b"));
            Assert.That(c0.IsSelected, Is.False);
            Assert.That(c1.IsSelected, Is.True);
            Assert.That(c2.IsSelected, Is.False);
        });
    }

    [Test]
    public void SelectedItem_SetsIndex()
    {
        var lb = MakeListBox("a", "b", "c");
        lb.SelectedItem = "c";
        Assert.That(lb.SelectedIndex, Is.EqualTo(2));
    }

    [Test]
    public void SelectFromContainer_SelectsThatItem()
    {
        var lb = MakeListBox("a", "b", "c");
        var c1 = (ListBoxItem)lb.ItemContainerGenerator.Realize(1);

        lb.SelectFromContainer(c1);   // simulates a press on the container

        Assert.Multiple(() =>
        {
            Assert.That(lb.SelectedIndex, Is.EqualTo(1));
            Assert.That(lb.SelectedItem, Is.EqualTo("b"));
            Assert.That(c1.IsSelected, Is.True);
        });
    }

    [Test]
    public void Selection_FollowsItem_WhenContainerRecycledAndRebound()
    {
        var lb = MakeListBox("a", "b", "c", "d", "e");
        var g = lb.ItemContainerGenerator;
        g.SetWindow(0, 1);            // realize items 0,1
        lb.SelectedIndex = 0;        // select "a"
        g.SetWindow(3, 4);           // scroll away: item 0's container is recycled (becomes a donor)
        g.SetWindow(0, 1);           // scroll back: a recycled container is rebound to item 0

        Assert.That(((ListBoxItem)g.ContainerFromIndex(0)).IsSelected, Is.True,
            "a recycled container rebound to the selected item shows selected - selection lives on the item, not the container");
    }

    [Test]
    public void Multiple_Click_TogglesItems_Cumulatively()
    {
        var lb = MakeListBox("a", "b", "c");
        lb.SelectionMode = SelectionMode.Multiple;
        var g = lb.ItemContainerGenerator;
        var c0 = (ListBoxItem)g.Realize(0);
        var c2 = (ListBoxItem)g.Realize(2);

        lb.SelectFromContainer(c0);   // no modifier -> add (not replace)
        lb.SelectFromContainer(c2);

        Assert.Multiple(() =>
        {
            Assert.That(c0.IsSelected, Is.True);
            Assert.That(c2.IsSelected, Is.True);
            Assert.That(lb.SelectedItems, Is.EquivalentTo(new[] { "a", "c" }));
        });

        lb.SelectFromContainer(c0);   // click again toggles it off
        Assert.That(c0.IsSelected, Is.False);
        Assert.That(lb.SelectedItems, Is.EquivalentTo(new[] { "c" }));
    }

    [Test]
    public void Extended_PlainClickReplaces_CtrlClickToggles()
    {
        var lb = MakeListBox("a", "b", "c", "d");
        lb.SelectionMode = SelectionMode.Extended;
        var g = lb.ItemContainerGenerator;
        var c0 = (ListBoxItem)g.Realize(0);
        var c1 = (ListBoxItem)g.Realize(1);
        var c3 = (ListBoxItem)g.Realize(3);

        lb.SelectFromContainer(c0);                               // plain -> only "a"
        lb.SelectFromContainer(c3, InputModifiers.LeftControl);   // ctrl  -> add "d"
        Assert.That(lb.SelectedItems, Is.EquivalentTo(new[] { "a", "d" }));

        lb.SelectFromContainer(c1);                               // plain -> replace with only "b"
        Assert.Multiple(() =>
        {
            Assert.That(lb.SelectedItems, Is.EquivalentTo(new[] { "b" }));
            Assert.That(c0.IsSelected, Is.False);
            Assert.That(c3.IsSelected, Is.False);
            Assert.That(c1.IsSelected, Is.True);
        });
    }

    [Test]
    public void Extended_ShiftClick_SelectsRangeFromAnchor()
    {
        var lb = MakeListBox("a", "b", "c", "d", "e");
        lb.SelectionMode = SelectionMode.Extended;
        var g = lb.ItemContainerGenerator;
        for (var i = 0; i < 5; i++) g.Realize(i);

        lb.SelectFromContainer((ListBoxItem)g.ContainerFromIndex(1));                            // anchor = "b"
        lb.SelectFromContainer((ListBoxItem)g.ContainerFromIndex(3), InputModifiers.LeftShift);  // range "b".."d"

        Assert.That(lb.SelectedItems, Is.EquivalentTo(new[] { "b", "c", "d" }));
    }

    [Test]
    public void SelectedItems_BoundCollection_ControlMutatesIt_ForViewModel()
    {
        var chosen = new ObservableCollection<object>();   // the view-model's own collection
        var lb = MakeListBox("a", "b", "c");
        lb.SelectionMode = SelectionMode.Multiple;
        lb.SelectedItems = chosen;                          // hand it to the control (a two-way binding does this)
        var c1 = (ListBoxItem)lb.ItemContainerGenerator.Realize(1);

        lb.SelectFromContainer(c1);                         // user selects

        Assert.That(chosen, Does.Contain("b"),
            "the control mutates the SAME collection the view-model holds - no attached-behavior workaround");
    }

    [Test]
    public void SelectedItems_TwoWayBinding_WinsOverDefault_AndControlUsesIt()
    {
        // Simulate a {Binding} (ValuePriority.Binding): it must take effect, so the control adopts the view-model's own
        // collection. Regression guard: a ctor-seeded default at Local priority would outrank Binding and silently
        // isolate the binding (SelectedItems would never reach the view-model -> the "Selected" list stays empty).
        var chosen = new ObservableCollection<object>();
        var lb = MakeListBox("a", "b", "c");
        lb.SelectionMode = SelectionMode.Multiple;
        lb.SetValue(ListBox.SelectedItemsProperty, chosen, ValuePriority.Binding);

        Assert.That(lb.SelectedItems, Is.SameAs(chosen), "the bound collection IS the control's SelectedItems");

        var c1 = (ListBoxItem)lb.ItemContainerGenerator.Realize(1);
        lb.SelectFromContainer(c1);
        Assert.That(chosen, Does.Contain("b"), "the control mutates the bound collection, not an internal default");
    }

    [Test]
    public void SelectedItems_BoundCollection_ViewModelMutatesIt_DrivesSelection()
    {
        var chosen = new ObservableCollection<object>();
        var lb = MakeListBox("a", "b", "c");
        lb.SelectionMode = SelectionMode.Multiple;
        lb.SelectedItems = chosen;
        var c2 = (ListBoxItem)lb.ItemContainerGenerator.Realize(2);

        chosen.Add("c");                                    // the view-model drives the selection

        Assert.Multiple(() =>
        {
            Assert.That(c2.IsSelected, Is.True, "mutating the bound collection selects the item in the control");
            Assert.That(lb.SelectedItem, Is.EqualTo("c"));
        });
    }

    // The view-model's every choice reaches the list, not only its first: the list published its own selection at Local
    // priority, which outranks the binding, so after the first selection the view-model could no longer move it.
    [Test]
    public void EveryChoiceOfTheViewModel_ReachesTheList()
    {
        var vm = new ChoiceVm { Chosen = "a" };
        vm.Items.Add("a");
        vm.Items.Add("b");
        vm.Items.Add("c");
        var lb = new ListBox { DataContext = vm };
        lb.SetBinding("ItemsSource", new Binding("Items"));
        lb.SetBinding("SelectedItem", new Binding("Chosen") { Mode = BindingMode.TwoWay });

        vm.Chosen = "c";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(lb.SelectedItem, Is.EqualTo("c"));
            Assert.That(lb.SelectedIndex, Is.EqualTo(2));
        });
    }

    // The choice bound before the items arrive: it must survive until they do, and the view-model must not be told
    // "nothing" in the meantime.
    [Test]
    public void AChoiceNamedBeforeTheItemsArrive_IsTheOneSelected()
    {
        var vm = new ChoiceVm { Chosen = "c" };
        vm.Items.Add("a");
        vm.Items.Add("b");
        vm.Items.Add("c");
        var lb = new ListBox { DataContext = vm };
        lb.SetBinding("SelectedItem", new Binding("Chosen") { Mode = BindingMode.TwoWay });
        lb.SetBinding("ItemsSource", new Binding("Items"));

        Assert.Multiple(() =>
        {
            Assert.That(lb.SelectedItem, Is.EqualTo("c"));
            Assert.That(lb.SelectedIndex, Is.EqualTo(2));
            Assert.That(vm.Chosen, Is.EqualTo("c"));
        });
    }

    // The selection is an ITEM: when items come and go above it, the index follows the item rather than staying on a
    // position that now holds something else.
    [Test]
    public void AnItemRemovedAboveTheSelection_KeepsTheSelectedItem()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var lb = new ListBox { ItemsSource = items };
        lb.SelectedItem = "c";

        items.Remove("a");

        Assert.Multiple(() =>
        {
            Assert.That(lb.SelectedItem, Is.EqualTo("c"));
            Assert.That(lb.SelectedIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void AnItemInsertedAboveTheSelection_KeepsTheSelectedItem()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var lb = new ListBox { ItemsSource = items };
        lb.SelectedItem = "b";

        items.Insert(0, "z");

        Assert.Multiple(() =>
        {
            Assert.That(lb.SelectedItem, Is.EqualTo("b"));
            Assert.That(lb.SelectedIndex, Is.EqualTo(2));
        });
    }

    // A selected item that leaves the list is no longer selected - and the view-model bound to the choice hears it, rather
    // than keeping an item the list no longer shows while the list lights up whatever took its place.
    [Test]
    public void TheSelectedItemLeavingTheList_LeavesNothingSelected()
    {
        var vm = new ChoiceVm { Chosen = "a" };
        vm.Items.Add("a");
        vm.Items.Add("b");
        var lb = new ListBox { DataContext = vm };
        lb.SetBinding("ItemsSource", new Binding("Items"));
        lb.SetBinding("SelectedItem", new Binding("Chosen") { Mode = BindingMode.TwoWay });

        vm.Items.Remove("a");

        Assert.Multiple(() =>
        {
            Assert.That(lb.SelectedItem, Is.Null);
            Assert.That(lb.SelectedIndex, Is.EqualTo(-1));
            Assert.That(vm.Chosen, Is.Null);
        });
    }

    [Test]
    public void AnItemLeavingAMultipleSelection_LeavesTheOthersSelected()
    {
        var items = new ObservableCollection<string> { "a", "b", "c" };
        var lb = new ListBox { ItemsSource = items, SelectionMode = SelectionMode.Multiple };
        var g = lb.ItemContainerGenerator;
        lb.SelectFromContainer((ListBoxItem)g.Realize(0));
        lb.SelectFromContainer((ListBoxItem)g.Realize(2));

        items.Remove("c");

        Assert.Multiple(() =>
        {
            Assert.That(lb.SelectedItems, Is.EquivalentTo(new[] { "a" }));
            Assert.That(lb.SelectedItem, Is.EqualTo("a"));
            Assert.That(lb.SelectedIndex, Is.EqualTo(0));
        });
    }

    private sealed class ChoiceVm : INotifyPropertyChanged
    {
        private string _chosen;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<string> Items { get; } = [];

        public string Chosen
        {
            get => _chosen;
            set
            {
                _chosen = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Chosen)));
            }
        }
    }

    [Test]
    public void MultiSelection_FollowsItems_AcrossRecycle()
    {
        var lb = MakeListBox("a", "b", "c", "d", "e");
        lb.SelectionMode = SelectionMode.Multiple;
        var g = lb.ItemContainerGenerator;
        g.SetWindow(0, 1);
        lb.SelectFromContainer((ListBoxItem)g.ContainerFromIndex(0));   // select "a"
        lb.SelectFromContainer((ListBoxItem)g.ContainerFromIndex(1));   // select "b"
        g.SetWindow(3, 4);                                              // scroll away (containers recycled)
        g.SetWindow(0, 1);                                              // scroll back (rebound)

        Assert.Multiple(() =>
        {
            Assert.That(((ListBoxItem)g.ContainerFromIndex(0)).IsSelected, Is.True);
            Assert.That(((ListBoxItem)g.ContainerFromIndex(1)).IsSelected, Is.True);
        });
    }

    private sealed class WatchedSelection : ObservableCollection<object>
    {
        public int ListBoxes;

        public override event System.Collections.Specialized.NotifyCollectionChangedEventHandler CollectionChanged
        {
            add
            {
                if (value.Target is ListBox)
                {
                    ListBoxes++;
                }

                base.CollectionChanged += value;
            }
            remove
            {
                if (value.Target is ListBox)
                {
                    ListBoxes--;
                }

                base.CollectionChanged -= value;
            }
        }
    }

    private sealed class SelectionHolder
    {
        public string[] Items { get; } = ["a", "b", "c"];

        public WatchedSelection Selection { get; } = new();
    }

    // Letting go is not undoing: the page is gone, the user's selection in the view-model is not.
    [Test]
    public void ADiscardedList_LeavesTheViewModelsSelectionAsItWas()
    {
        var holder = new SelectionHolder();
        holder.Selection.Add("b");
        var lb = new ListBox { DataContext = holder, SelectionMode = SelectionMode.Multiple };
        lb.SetBinding("ItemsSource", new Binding("Items"));
        lb.SetBinding("SelectedItems", new Binding("Selection"));
        Assert.That(lb.SelectedItem, Is.EqualTo("b"), "the list took the bound selection");

        DiscardedVisuals.Publish(lb);
        DiscardedVisuals.Drain(int.MaxValue);

        Assert.That(holder.Selection, Is.EqualTo(new[] { "b" }));
    }

    // The view-model's selection outlives the page that shows it. A list thrown away with its page has to stop listening
    // to it, or the view-model keeps the list - and the whole page - for as long as the application runs.
    [Test]
    public void ADiscardedList_LetsGoOfTheBoundSelection()
    {
        var holder = new SelectionHolder();
        var lb = new ListBox { ItemsSource = new[] { "a", "b" }, DataContext = holder };
        lb.SetBinding("SelectedItems", new Binding("Selection"));
        Assert.That(holder.Selection.ListBoxes, Is.EqualTo(1), "the list follows the bound selection");

        DiscardedVisuals.Publish(lb);
        DiscardedVisuals.Drain(int.MaxValue);

        Assert.That(holder.Selection.ListBoxes, Is.Zero, "a discarded list stops listening");
    }
}
