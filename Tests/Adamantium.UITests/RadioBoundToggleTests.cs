using System.ComponentModel;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

// Two radios bound to "am I the one" properties whose source ignores false: a cleared radio must not be re-checked from
// the source, or the pair stops switching.
[TestFixture]
public class RadioBoundToggleTests
{
    private sealed class Faces : INotifyPropertyChanged
    {
        private bool _graph;

        public bool IsDrawing
        {
            get => !_graph;
            set { if (value) Graph = false; }
        }

        public bool IsGraph
        {
            get => _graph;
            set { if (value) Graph = true; }
        }

        private bool Graph
        {
            set
            {
                if (_graph == value) return;

                _graph = value;
                Raise(nameof(IsDrawing));
                Raise(nameof(IsGraph));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static RadioButton Bound(Faces faces, string path, string group)
    {
        var button = new RadioButton { GroupName = group };

        button.SetBinding(ToggleButton.IsCheckedProperty, new Binding(path)
        {
            Source = faces,
            Mode = BindingMode.TwoWay
        });

        return button;
    }

    private static void Settle() => BindingUpdateQueue.Flush();

    // A group name is app-wide, so each test needs its own.
    private static (Faces Faces, RadioButton Drawing, RadioButton Graph) Pair(string group)
    {
        var faces = new Faces();
        var drawing = Bound(faces, nameof(Faces.IsDrawing), group);
        var graph = Bound(faces, nameof(Faces.IsGraph), group);

        Settle();

        return (faces, drawing, graph);
    }

    [Test]
    public void PressingTheOtherOneSwitchesTheChoice()
    {
        var (faces, drawing, graph) = Pair("Faces.Switching");

        Assert.Multiple(() =>
        {
            Assert.That(drawing.IsChecked, Is.True, "the choice in force is not shown as such");
            Assert.That(graph.IsChecked, Is.False);
        });

        graph.PerformClick();
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(faces.IsGraph, Is.True, "pressing the other choice did not take");
            Assert.That(graph.IsChecked, Is.True);
            Assert.That(drawing.IsChecked, Is.False, "both halves of one choice are lit");
        });

        // ...and BACK, which is what stops working when the cleared button pushes itself on again.
        drawing.PerformClick();
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(faces.IsDrawing, Is.True, "the pair stopped switching after one change");
            Assert.That(drawing.IsChecked, Is.True);
            Assert.That(graph.IsChecked, Is.False);
        });
    }

    [Test]
    public void PressingTheOneAlreadyChosenChangesNothing()
    {
        var (faces, drawing, graph) = Pair("Faces.Standing");

        drawing.PerformClick();
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(faces.IsDrawing, Is.True, "a press on the choice in force changed it");
            Assert.That(drawing.IsChecked, Is.True, "the button came unpressed while the choice stayed");
            Assert.That(graph.IsChecked, Is.False);
        });
    }
}
