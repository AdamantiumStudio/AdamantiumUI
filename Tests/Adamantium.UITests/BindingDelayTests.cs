using System.ComponentModel;
using System.Linq;
using System.Threading;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A two-way binding's Delay: the source is written once the target's changes stop for that long - a search
/// typed letter by letter, filtered once - and at once when the keyboard leaves, when told, or when the binding lets its
/// source go, into the source it was typed for; a source that moves meanwhile drops what waited.</summary>
[TestFixture]
[NonParallelizable]
public class BindingDelayTests
{
    // Long beside the waits that must come in under it, so a slow machine does not fire it early.
    private const int Delay = 300;

    private sealed class Item : INotifyPropertyChanged
    {
        private string _name = "old";

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                Writes++;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public int Writes { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    private sealed class Chooser : INotifyPropertyChanged
    {
        private Item _selected;

        public Item Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    private Item _item;
    private TextBox _box;
    private TextBox _other;

    // The UI's thread is the loop's, as in an application: the delay's tick comes on it.
    [SetUp]
    public void Bind()
    {
        BindingUpdateQueue.Flush();
        LoopSignal.Claim();
        _item = new Item();
        _box = new TextBox { DataContext = _item };
        _other = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(_box);
        panel.Children.Add(_other);
        _box.SetBinding("Text", new Binding("Name") { Mode = BindingMode.TwoWay, Delay = Delay });
    }

    // The binding closed while the loop is still the test's, so no wait of it ticks into the next test.
    [TearDown]
    public void LetGo()
    {
        BindingEngine.ClearBindings(_box);
        FocusManager.Focus(null);
        LoopSignal.Unclaim();
    }

    private static void Wait(int milliseconds)
    {
        Thread.Sleep(milliseconds);
        LoopSignal.Drain();
        BindingUpdateQueue.Flush();
    }

    // Typed, as a text box writes what is typed: under the binding, which goes on showing its source.
    private void Type(string text) => _box.SetCurrentValue(TextBox.TextProperty, text);

    [Test]
    public void Changes_AreWrittenOnce_WhenTheyStop()
    {
        Type("a");
        Type("ab");
        Type("abc");
        var meanwhile = _item.Name;
        Wait(Delay * 2);

        Assert.Multiple(() =>
        {
            Assert.That(meanwhile, Is.EqualTo("old"), "nothing while the changes come");
            Assert.That(_item.Name, Is.EqualTo("abc"));
            Assert.That(_item.Writes, Is.EqualTo(1), "one write for them all");
        });
    }

    [Test]
    public void EachChange_StartsTheWaitAgain()
    {
        Type("a");
        Wait(Delay / 2);
        Type("ab");
        Wait(Delay / 2);
        var waitedAgain = _item.Name;
        Wait(Delay * 2);

        Assert.Multiple(() =>
        {
            Assert.That(waitedAgain, Is.EqualTo("old"), "the delay since the first change, not since the last");
            Assert.That(_item.Name, Is.EqualTo("ab"));
        });
    }

    [Test]
    public void TheKeyboardLeaving_WritesAtOnce()
    {
        FocusManager.Focus(_box);
        Type("typed");
        FocusManager.Focus(_other);

        Assert.That(_item.Name, Is.EqualTo("typed"), "no wait once the focus left");
    }

    [Test]
    public void Told_ItWritesAtOnce_AndNotAgainAfter()
    {
        Type("typed");
        BindingEngine.GetBindings(_box).Single().UpdateSource();
        var told = _item.Name;
        Wait(Delay * 2);

        Assert.Multiple(() =>
        {
            Assert.That(told, Is.EqualTo("typed"));
            Assert.That(_item.Writes, Is.EqualTo(1), "the wait ended with the write");
        });
    }

    // Another DataContext - a row recycled while typed in: what was typed is the old item's, written there, not the new
    // one's.
    [Test]
    public void AnotherSource_TheLastChangeGoesToTheOld()
    {
        Type("typed");
        var next = new Item();
        _box.DataContext = next;
        Wait(Delay * 2);

        Assert.Multiple(() =>
        {
            Assert.That(_item.Name, Is.EqualTo("typed"), "the change waiting went to the source it was made for");
            Assert.That(next.Name, Is.EqualTo("old"), "the new source is not written with it");
            Assert.That(_box.Text, Is.EqualTo("old"), "the box shows the new source");
        });
    }

    // Through a path, an object on it replaced while typed - another item chosen: the change goes to the item it was
    // typed for.
    [Test]
    public void AnObjectOnThePathReplaced_TheLastChangeGoesToTheOld()
    {
        var first = new Item();
        var second = new Item();
        var chooser = new Chooser { Selected = first };
        var box = new TextBox { DataContext = chooser };
        box.SetBinding("Text", new Binding("Selected.Name") { Mode = BindingMode.TwoWay, Delay = Delay });
        try
        {
            box.SetCurrentValue(TextBox.TextProperty, "typed");
            chooser.Selected = second;
            Wait(Delay * 2);

            Assert.Multiple(() =>
            {
                Assert.That(first.Name, Is.EqualTo("typed"), "the item typed for");
                Assert.That(second.Name, Is.EqualTo("old"), "the item chosen after is not written with it");
                Assert.That(box.Text, Is.EqualTo("old"), "the box shows the item chosen");
            });
        }
        finally
        {
            BindingEngine.ClearBindings(box);
        }
    }

    // The source moved while a change waited: the box shows the source's value, and the change - made against what it
    // held before - is not written over it.
    [Test]
    public void TheSourceMoving_DropsWhatWaited()
    {
        Type("typed");
        _item.Name = "moved";
        var writes = _item.Writes;
        Wait(Delay * 2);

        Assert.Multiple(() =>
        {
            Assert.That(_item.Name, Is.EqualTo("moved"));
            Assert.That(_item.Writes, Is.EqualTo(writes), "nothing written after the source moved");
            Assert.That(_box.Text, Is.EqualTo("moved"), "the box shows where the source went");
        });
    }
}
