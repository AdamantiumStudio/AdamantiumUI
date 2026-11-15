using System.ComponentModel;
using System.Threading;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A binding whose source lives on another thread - a game world's - writes it there
/// (<see cref="Binding.WorkOnSourceThread"/>, the thread named by the tree's <see cref="BindingEngine.SourceThreadProperty"/>),
/// and a change the source tells on its thread reaches the target on the UI's.</summary>
[TestFixture]
public class SourceThreadBindingTests
{
    private sealed class Model : INotifyPropertyChanged
    {
        private string _name = "A";
        private Vector2<double> _offset;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public Vector2<double> Offset
        {
            get => _offset;
            set
            {
                _offset = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Offset)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    // Takes a name in capitals only, as a source that clamps what it is given.
    private sealed class ShoutingModel : INotifyPropertyChanged
    {
        private string _name = "A";

        public string Name
        {
            get => _name;
            set
            {
                _name = value?.ToUpperInvariant();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    [Test]
    public void AWriteBack_WaitsForTheSourcesThread_AndTakesTheLatestValue()
    {
        var model = new Model();
        var thread = new QueuedSourceThread();
        var target = Bound(model, "Name", thread);

        target.SetCurrentValue(TextBlock.TextProperty, "B");
        target.SetCurrentValue(TextBlock.TextProperty, "C");
        var before = model.Name;
        var waiting = thread.Waiting;
        thread.RunAll();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo("A"), "written on the UI's thread");
            Assert.That(waiting, Is.EqualTo(1), "one update for both values");
            Assert.That(model.Name, Is.EqualTo("C"));
            Assert.That(target.Text, Is.EqualTo("C"));
        });
    }

    [Test]
    public void WithoutASourceThreadInTheTree_TheSourceIsWrittenAtOnce()
    {
        var model = new Model();
        var target = Bound(model, "Name", null);

        target.SetCurrentValue(TextBlock.TextProperty, "B");

        Assert.That(model.Name, Is.EqualTo("B"));
    }

    // A view model beside the world's objects in the same panel lives on the UI's thread, which the source's thread says.
    [Test]
    public void ASourceTheThreadDoesNotHold_IsWrittenAtOnce()
    {
        var model = new Model();
        var thread = new QueuedSourceThread { Holding = _ => false };
        var target = Bound(model, "Name", thread);

        target.SetCurrentValue(TextBlock.TextProperty, "B");

        Assert.Multiple(() =>
        {
            Assert.That(model.Name, Is.EqualTo("B"));
            Assert.That(thread.Waiting, Is.Zero);
        });
    }

    // A number of a struct is written as the whole struct, read and put back on the source's thread.
    [Test]
    public void AStructsMember_IsWrittenOnTheSourcesThread()
    {
        var model = new Model { Offset = new Vector2<double>(1, 2) };
        var thread = new QueuedSourceThread();
        var target = Bound(model, "Offset.X", thread);

        target.SetCurrentValue(TextBlock.TextProperty, "5");
        var before = model.Offset;
        thread.RunAll();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(new Vector2<double>(1, 2)));
            Assert.That(model.Offset, Is.EqualTo(new Vector2<double>(5, 2)));
        });
    }

    // A row of the inspector reads its object immediately, and that object told a change on its own thread: the row
    // changed there, off the UI's thread.
    [Test]
    public void AChangeToldOnAnotherThread_ReachesAnImmediateBinding_OnTheFlush()
    {
        var model = new Model();
        var target = new TextBlock();
        target.SetBinding("Text", new Binding("Name") { Source = model, IsImmediate = true });

        Told(() => model.Name = "B");
        var beforeFlush = target.Text;
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(beforeFlush, Is.EqualTo("A"), "changed on the source's thread");
            Assert.That(target.Text, Is.EqualTo("B"));
        });
    }

    // A number dragged: the source moved on its own before the dragged value reached it, and the field is not thrown back
    // to the source's value meanwhile.
    [Test]
    public void WhileAWriteWaits_TheSourcesOwnChange_DoesNotThrowTheTargetBack()
    {
        var model = new Model();
        var thread = new QueuedSourceThread();
        var target = Bound(model, "Name", thread);

        target.SetCurrentValue(TextBlock.TextProperty, "B");
        Told(() => model.Name = "X");
        BindingUpdateQueue.Flush();
        var meanwhile = target.Text;
        thread.RunAll();
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(meanwhile, Is.EqualTo("B"));
            Assert.That(model.Name, Is.EqualTo("B"));
            Assert.That(target.Text, Is.EqualTo("B"));
        });
    }

    [Test]
    public void ASourceThatTookAnotherValue_IsShownOnTheFlush()
    {
        var model = new ShoutingModel();
        var thread = new QueuedSourceThread();
        var holder = new Border();
        BindingEngine.SetSourceThread(holder, thread);
        var target = new TextBlock();
        holder.Child = target;
        target.SetBinding("Text", new Binding("Name") { Source = model, Mode = BindingMode.TwoWay, WorkOnSourceThread = true });

        target.SetCurrentValue(TextBlock.TextProperty, "b");
        thread.RunAll();
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(model.Name, Is.EqualTo("B"));
            Assert.That(target.Text, Is.EqualTo("B"));
        });
    }

    // A text block under a border that names the source's thread: the tree passes it down.
    private static TextBlock Bound(object model, string path, ISourceThread thread)
    {
        var holder = new Border();
        BindingEngine.SetSourceThread(holder, thread);
        var target = new TextBlock();
        holder.Child = target;
        target.SetBinding("Text", new Binding(path) { Source = model, Mode = BindingMode.TwoWay, WorkOnSourceThread = true });
        return target;
    }

    private static void Told(System.Action change)
    {
        var thread = new Thread(() => change());
        thread.Start();
        thread.Join();
    }
}
