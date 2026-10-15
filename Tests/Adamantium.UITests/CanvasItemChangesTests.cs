using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>AN ITEM SAYS WHEN IT IS MOVED OR RESIZED, and says only that. An inspector bound to the selection follows a
/// drag, a grip and an undo through the item itself - nothing has to remember to tell it.</summary>
[TestFixture]
public class CanvasItemChangesTests
{
    private static List<string> Heard(ICanvasItem item)
    {
        var heard = new List<string>();
        item.PropertyChanged += (_, e) => heard.Add(e.PropertyName);
        return heard;
    }

    private static IEnumerable<TestCaseData> Placed()
    {
        yield return new TestCaseData(new ShapeItem(CanvasShape.Rectangle, new Rect(0, 0, 40, 20), Brushes.White, 1))
            .SetName("Shape");
        yield return new TestCaseData(Stroke()).SetName("Stroke");
        yield return new TestCaseData(new TextItem(new Vector2(0, 0), "words", Brushes.White, 12)).SetName("Text");
        yield return new TestCaseData(new PathItem("M0,0 L40,20", null, Brushes.White)).SetName("Path");
        yield return new TestCaseData(new ElementItem(new Border(), new Rect(0, 0, 40, 20))).SetName("Element");
        yield return new TestCaseData(new GroupItem([Stroke()])).SetName("Group");
    }

    private static StrokeItem Stroke()
    {
        var stroke = new StrokeItem(new Vector2(0, 0), Brushes.White, 2);
        stroke.Add(new Vector2(0, 0));
        stroke.Add(new Vector2(40, 20));
        return stroke;
    }

    [TestCaseSource(nameof(Placed))]
    public void MovingSaysWhereItNowIs(ICanvasItem item)
    {
        var heard = Heard(item);

        item.Move(new Vector2(5, 7));

        Assert.That(heard, Does.Contain("X").And.Contain("Y").And.Contain("Bounds"));
    }

    // NOTHING MOVED, nothing said: a line written back with the number it already shows is not a change.
    [TestCaseSource(nameof(Placed))]
    public void StayingPutSaysNothing(ICanvasItem item)
    {
        var heard = Heard(item);

        item.Move(new Vector2(0, 0));

        Assert.That(heard, Is.Empty);
    }

    [Test]
    public void ResizingAShapeSaysItsNewSize()
    {
        var shape = new ShapeItem(CanvasShape.Rectangle, new Rect(0, 0, 40, 20), Brushes.White, 1);
        var heard = Heard(shape);

        shape.Resize(new Rect(0, 0, 80, 60));

        Assert.That(heard, Does.Contain("Width").And.Contain("Height"));
    }

    // A STROKE made bigger is drawn with a pen made bigger - so its thickness is news too.
    [Test]
    public void ResizingAStrokeSaysItsNewThickness()
    {
        var stroke = Stroke();
        var heard = Heard(stroke);

        stroke.Resize(new Rect(0, 0, 160, 80));

        Assert.That(heard, Does.Contain("Width").And.Contain("Height").And.Contain("Thickness"));
    }

    [Test]
    public void ACurveAndAFrameSayTheirBox()
    {
        var curve = new CurveItem(CanvasCurve.Bezier, [new Vector2(0, 0), new Vector2(40, 20)], Brushes.White, 2);
        var frame = new CanvasFrameItem(new Rect(0, 0, 200, 100), "Notes", Brushes.White);
        var curveHeard = Heard(curve);
        var frameHeard = Heard(frame);

        curve.MovePoint(1, new Vector2(60, 30));
        frame.Move(new Vector2(10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(curveHeard, Does.Contain("Bounds"), "a curve whose point moved");
            Assert.That(frameHeard, Does.Contain("Bounds"), "a frame that was moved");
        });
    }

    private static PropertyGrid Inspecting(object target, params PropertyDefinition[] lines)
    {
        var section = new PropertySection { Header = "Item", Target = target, IsExpanded = true };
        foreach (var line in lines) section.Properties.Add(line);

        var grid = new PropertyGrid { Template = Chrome() };
        grid.Sections.Add(section);
        grid.Measure(new Size(400, 400), force: true);
        grid.Arrange(new Rect(0, 0, 400, 400));
        return grid;
    }

    private static NumericProperty Line(string name) => new() { Header = name, Binding = new Binding(name) };

    // What the LINE shows - not what the object holds: asked of the object, the answer is right whether or not the line
    // ever heard of the change.
    private static object Shown(PropertyGrid grid, PropertyDefinition line)
    {
        foreach (var section in grid.Sections)
        {
            if (section.Content is not IUIComponent host)
            {
                continue;
            }

            foreach (var child in host.VisualChildren)
            {
                if (child is PropertyRow row && ReferenceEquals(row.Definition, line))
                {
                    return row.Value;
                }
            }
        }

        return null;
    }

    // THE BUG ITSELF: a line of an inspector showing where a selected item is, and the item dragged.
    [Test]
    public void ALineBoundToAnItemFollowsADrag()
    {
        var shape = new ShapeItem(CanvasShape.Rectangle, new Rect(0, 0, 40, 20), Brushes.White, 1);
        var x = Line("X");
        var grid = Inspecting(shape, x);

        Assert.That(Shown(grid, x), Is.EqualTo(0.0), "the line was not showing the item to begin with");

        shape.Move(new Vector2(25, 0));

        Assert.That(Shown(grid, x), Is.EqualTo(25.0));
    }

    // A NUMBER TYPED IN does not stop the line following its object: written over the line's binding, it outranked
    // everything the object said afterwards, and a dragged item left the line showing what was typed.
    [Test]
    public void ALineTypedIntoStillFollowsADrag()
    {
        var shape = new ShapeItem(CanvasShape.Rectangle, new Rect(0, 0, 40, 20), Brushes.White, 1);
        var x = Line("X");
        var grid = Inspecting(shape, x);
        var row = Row(grid, x);

        Assert.That(grid.Write(row, 100.0), Is.True, "the line refused the number");

        shape.Move(new Vector2(10, 0));

        Assert.That(Shown(grid, x), Is.EqualTo(110.0));
    }

    // A ROW STANDING FOR MANY asks again only the one value that moved. Every one of a thousand objects moved by one
    // drag made the row compare all thousand - a second a step.
    [Test]
    public void AManyObjectLineSaysWhetherTheyAgreeAsEachMoves()
    {
        var shapes = Enumerable.Range(0, 4)
            .Select(i => new ShapeItem(CanvasShape.Rectangle, new Rect(i * 10, 0, 40, 20), Brushes.White, 1))
            .ToList();
        var y = Line("Y");
        var grid = new PropertyGrid { Template = Chrome() };
        var section = new PropertySection { Header = "Item", IsExpanded = true };
        section.Properties.Add(y);
        grid.Sections.Add(section);
        grid.SelectedObjects = shapes;
        grid.Measure(new Size(400, 400), force: true);
        grid.Arrange(new Rect(0, 0, 400, 400));
        var row = Row(grid, y);

        Assert.That(row.IsMixed, Is.False, "four at the same height read as disagreeing");

        shapes[2].Move(new Vector2(0, 5));
        Assert.That(row.IsMixed, Is.True, "one moved away and they still read as agreeing");

        shapes[2].Move(new Vector2(0, -5));
        Assert.That(row.IsMixed, Is.False, "back in line and they still read as disagreeing");

        foreach (var shape in shapes) shape.Move(new Vector2(0, 7));
        Assert.Multiple(() =>
        {
            Assert.That(row.IsMixed, Is.False, "all moved together and they read as disagreeing");
            Assert.That(row.Value, Is.EqualTo(7.0), "all moved together and the line kept the old height");
        });
    }

    private static PropertyRow Row(PropertyGrid grid, PropertyDefinition line)
    {
        foreach (var section in grid.Sections)
        {
            if (section.Content is not IUIComponent host)
            {
                continue;
            }

            foreach (var child in host.VisualChildren)
            {
                if (child is PropertyRow row && ReferenceEquals(row.Definition, line))
                {
                    return row;
                }
            }
        }

        return null;
    }

    // A LINE WRITES BACK what it was told, and the item it writes into may say so again before the first word has
    // reached every line. Each telling has to reach every line all the same - the second one emptied the list the first
    // was still going down, and a resize threw.
    [Test]
    public void ResizingUnderOpenSizeLinesReachesEveryLine()
    {
        var stroke = Stroke();
        var group = new GroupItem([Stroke()]);
        var strokeWidth = Line("Width");
        var strokeHeight = Line("Height");
        var groupWidth = Line("Width");
        var groupHeight = Line("Height");
        var strokes = Inspecting(stroke, strokeWidth, strokeHeight);
        var groups = Inspecting(group, groupWidth, groupHeight);

        Assert.DoesNotThrow(() => stroke.Resize(new Rect(0, 0, 160, 80)), "resizing a stroke");
        Assert.DoesNotThrow(() => stroke.MoveLast(new Vector2(300, 200)), "pulling a stroke's last point");
        Assert.DoesNotThrow(() => stroke.RemoveLast(), "taking a stroke's last point away");
        Assert.DoesNotThrow(() => group.Resize(new Rect(0, 0, 160, 80)), "resizing a group");

        Assert.Multiple(() =>
        {
            Assert.That(Shown(strokes, strokeWidth), Is.EqualTo(stroke.Width), "stroke width");
            Assert.That(Shown(strokes, strokeHeight), Is.EqualTo(stroke.Height), "stroke height");
            Assert.That(Shown(groups, groupWidth), Is.EqualTo(group.Width), "group width");
            Assert.That(Shown(groups, groupHeight), Is.EqualTo(group.Height), "group height");
        });
    }

    private static Adamantium.UI.Core.Templates.ControlTemplate Chrome() =>
        new(() =>
        {
            var host = new Adamantium.UI.Controls.Panels.StackPanel();
            var result = new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = host };
            result.RegisterName("PART_Sections", host);
            return result;
        });

    private sealed class Counted : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler _changed;

        public int Listeners { get; private set; }

        public string Name { get; set; } = "one";

        public event PropertyChangedEventHandler PropertyChanged
        {
            add
            {
                _changed += value;
                Listeners++;
            }
            remove
            {
                _changed -= value;
                Listeners--;
            }
        }
    }

    private sealed class Echoing : INotifyPropertyChanged
    {
        private string _name = "one";

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    // A SOURCE TOLD AGAIN while it is still telling: the first binding writes back, the source says so once more, and
    // that second telling must not take the list of bindings away from the first.
    [Test]
    public void ASourceToldAgainMidwayStillReachesEveryBinding()
    {
        var source = new Echoing();
        var first = new TextBox();
        var second = new TextBox();

        BindingEngine.SetBinding(first, TextBox.TextProperty,
            new Binding("Name") { Source = source, Mode = BindingMode.TwoWay, IsImmediate = true });
        BindingEngine.SetBinding(second, TextBox.TextProperty,
            new Binding("Name") { Source = source, Mode = BindingMode.TwoWay, IsImmediate = true });

        Assert.DoesNotThrow(() => source.Name = "two");

        Assert.Multiple(() =>
        {
            Assert.That(first.Text, Is.EqualTo("two"), "first");
            Assert.That(second.Text, Is.EqualTo("two"), "second");
        });
    }

    // NO SUBSCRIPTION OUTLIVES THE LAST BINDING. An inspector binds to what is selected and lets go when the selection
    // moves on; a source that kept its listener after that would collect one for every item ever selected.
    [Test]
    public void ASourceIsLetGoWhenItsLastBindingIs()
    {
        var source = new Counted();
        var first = new TextBlock();
        var second = new TextBlock();

        BindingEngine.SetBinding(first, TextBlock.TextProperty, new Binding("Name") { Source = source });
        BindingEngine.SetBinding(second, TextBlock.TextProperty, new Binding("Name") { Source = source });

        Assert.That(source.Listeners, Is.EqualTo(1), "two bindings, one subscription");

        BindingEngine.ClearBindings(first);

        Assert.That(source.Listeners, Is.EqualTo(1), "let go while a binding still reads it");

        BindingEngine.ClearBindings(second);

        Assert.That(source.Listeners, Is.Zero, "still held after its last binding went");

        BindingEngine.SetBinding(first, TextBlock.TextProperty, new Binding("Name") { Source = source });

        Assert.That(source.Listeners, Is.EqualTo(1), "a source let go of is not taken up again");
    }
}
