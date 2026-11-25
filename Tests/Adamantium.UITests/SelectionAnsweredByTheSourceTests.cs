using Adamantium.Core.Collections;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

// The property a selection is bound to answers the choice while it is being written: it refuses it, names another, or
// changes the very items. What the control selects is what was answered.
public class SelectionAnsweredByTheSourceTests
{
    public sealed class Vm : System.ComponentModel.INotifyPropertyChanged
    {
        private object _selected;

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public System.Func<object, object> Answer { get; set; } = value => value;

        public object Selected
        {
            get => _selected;
            set
            {
                _selected = Answer(value);
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Selected)));
            }
        }
    }

    private static (T Control, Vm Vm, CollectionView View) Bound<T>() where T : Selector, new()
    {
        var view = new CollectionView(new TrackingCollection<string>(["a", "b", "c"]));
        var vm = new Vm();
        var control = new T { DataContext = vm, ItemsSource = view };
        control.SetBinding("SelectedItem", new Binding("Selected") { Mode = BindingMode.TwoWay });
        return (control, vm, view);
    }

    [TestCase(typeof(ListBox))]
    [TestCase(typeof(DropDown))]
    public void AChoiceRefused_IsNotSelected(System.Type type)
    {
        var (control, vm, view) = type == typeof(ListBox) ? Boxed(Bound<ListBox>()) : Boxed(Bound<DropDown>());
        vm.Answer = _ => null;

        control.SelectedIndex = 1;

        Assert.Multiple(() =>
        {
            Assert.That(control.SelectedItem, Is.Null);
            Assert.That(control.SelectedIndex, Is.EqualTo(-1));
        });

        view.Refresh();

        Assert.That(control.SelectedItem, Is.Null, "and the items made anew do not select it again");
        Assert.That(control.SelectedIndex, Is.EqualTo(-1));
    }

    [TestCase(typeof(ListBox))]
    [TestCase(typeof(DropDown))]
    public void AnotherChoiceNamed_IsTheOneSelected(System.Type type)
    {
        var (control, vm, _) = type == typeof(ListBox) ? Boxed(Bound<ListBox>()) : Boxed(Bound<DropDown>());
        vm.Answer = value => Equals(value, "b") ? "c" : value;

        control.SelectedIndex = 1;

        Assert.Multiple(() =>
        {
            Assert.That(control.SelectedItem, Is.EqualTo("c"));
            Assert.That(control.SelectedIndex, Is.EqualTo(2));
        });
    }

    [Test]
    public void AListRefusedAChoice_ShowsNoRowSelected()
    {
        var (list, vm, _) = Bound<ListBox>();
        var row = (ListBoxItem)list.ItemContainerGenerator.Realize(1);
        vm.Answer = _ => null;

        list.SelectFromContainer(row);

        Assert.That(row.IsSelected, Is.False);
        Assert.That(list.SelectedItems, Is.Null.Or.Empty);
    }

    // A source that never agrees is asked once, not forever.
    [TestCase(typeof(ListBox))]
    [TestCase(typeof(DropDown))]
    public void ASourceThatKeepsWhatIsGone_IsNotAskedForever(System.Type type)
    {
        var (control, vm, view) = type == typeof(ListBox) ? Boxed(Bound<ListBox>()) : Boxed(Bound<DropDown>());
        control.SelectedIndex = 1;
        vm.Answer = value => value ?? "b";

        view.Filter = item => item is not "b";
        control.SelectedIndex = -1;

        Assert.That(control.SelectedIndex, Is.EqualTo(-1));
    }

    [TestCase(typeof(ListBox))]
    [TestCase(typeof(DropDown))]
    public void AChoiceTaken_WithARowPutInBeforeIt_StaysTheChoice(System.Type type)
    {
        var (control, vm, view) = type == typeof(ListBox) ? Boxed(Bound<ListBox>()) : Boxed(Bound<DropDown>());
        control.SelectedIndex = 0;
        var changes = 0;
        control.SelectionChanged += (_, _) => changes++;
        vm.Answer = value =>
        {
            vm.Answer = same => same;
            ((TrackingCollection<string>)view.Source).Insert(0, "new");
            return value;
        };

        control.SelectedIndex = 1;

        Assert.Multiple(() =>
        {
            Assert.That(control.SelectedItem, Is.EqualTo("b"));
            Assert.That(control.SelectedIndex, Is.EqualTo(2));
            Assert.That(vm.Selected, Is.EqualTo("b"));
            Assert.That(changes, Is.GreaterThan(0));
            if (control is ListBox list) Assert.That(list.SelectedItems, Is.EqualTo(new[] { "b" }));
        });
    }

    [TestCase(typeof(ListBox))]
    [TestCase(typeof(DropDown))]
    public void AChoiceTakenOutOfTheItems_IsNotLeftSelected(System.Type type)
    {
        var (control, vm, view) = type == typeof(ListBox) ? Boxed(Bound<ListBox>()) : Boxed(Bound<DropDown>());
        vm.Answer = value =>
        {
            if (value != null) ((TrackingCollection<string>)view.Source).Remove((string)value);
            return value;
        };

        control.SelectedIndex = 1;

        Assert.That(control.SelectedItem, Is.Null);
        Assert.That(control.SelectedIndex, Is.EqualTo(-1));
    }

    [Test]
    public void ARowRefusedToSeveralSelected_LeavesThemSelected()
    {
        var (list, vm, _) = Bound<ListBox>();
        list.SelectionMode = SelectionMode.Multiple;
        list.SelectFromContainer((ListBoxItem)list.ItemContainerGenerator.Realize(0));
        list.SelectFromContainer((ListBoxItem)list.ItemContainerGenerator.Realize(1));
        vm.Answer = _ => "b";

        list.SelectFromContainer((ListBoxItem)list.ItemContainerGenerator.Realize(2));

        Assert.That(list.SelectedItems, Is.EquivalentTo(new[] { "a", "b" }));
        Assert.That(list.SelectedItem, Is.EqualTo("b"));
    }

    [Test]
    public void ItemsMadeAnewByTheAnswer_LeaveTheSelectionAgreeing()
    {
        var (list, vm, view) = Bound<ListBox>();
        vm.Answer = value =>
        {
            if (value == null) return null;

            view.Filter = item => item is "c";
            return null;
        };

        list.SelectedIndex = 1;
        view.Filter = null;

        Assert.That(list.SelectedItem, Is.Null);
        Assert.That(list.SelectedIndex, Is.EqualTo(-1));
    }

    private static (Selector, Vm, CollectionView) Boxed<T>((T Control, Vm Vm, CollectionView View) bound) where T : Selector =>
        (bound.Control, bound.Vm, bound.View);
}
