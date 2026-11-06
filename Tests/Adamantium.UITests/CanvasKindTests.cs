using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A board and a graph are two controls on one plane. What draws belongs to the board and what joins belongs
/// to the graph: a tool of the one, handed to the other, does nothing rather than leave something neither shows.</summary>
public class CanvasKindTests
{
    private static T Sized<T>() where T : InfiniteCanvas, new()
    {
        var canvas = new T { Scene = new CanvasScene() };
        canvas.Measure(new Size<double>(800, 600), force: true);
        canvas.Arrange(new Rect(0, 0, 800, 600));

        return canvas;
    }

    private static CanvasPointerEventArgs At(double x, double y) => new()
    {
        World = new Vector2<double>(x, y),
        Pointer = new Vector2<double>(x, y),
        Screen = new Vector2<double>(x, y),
        Button = MouseButtons.Left,
        ClickCount = 1
    };

    private static void Stroke(ICanvasTool pen, InfiniteCanvas canvas)
    {
        pen.OnPressed(canvas, At(10, 10));
        pen.OnMoved(canvas, At(60, 40));
        pen.OnMoved(canvas, At(120, 80));
        pen.OnReleased(canvas, At(120, 80));
    }

    [Test]
    public void ABoardIsADrawingAndAGraphIsNot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new DrawingBoard().IsDrawing, Is.True);
            Assert.That(new NodeGraph().IsDrawing, Is.False);
        });
    }

    [Test]
    public void APenDrawsOnABoard()
    {
        var board = Sized<DrawingBoard>();
        board.Ink = Brushes.White;

        Stroke(new PenTool(), board);

        Assert.That(((CanvasScene)board.Scene).Items, Has.Count.EqualTo(1), "the pen left nothing on the board");
    }

    // A graph has no ink to draw with, so a pen given to it is a pen that does nothing.
    [Test]
    public void APenOnAGraphLeavesNothing()
    {
        var graph = Sized<NodeGraph>();

        Stroke(new PenTool(), graph);

        Assert.That(((CanvasScene)graph.Scene).Items, Is.Empty, "a pen drew into a graph");
    }

    // An eraser handed to a graph rubbed out whatever lay under the press - a node, a wire.
    [Test]
    [TestCase(CanvasEraseMode.Point)]
    [TestCase(CanvasEraseMode.Stroke)]
    public void AnEraserOnAGraphTakesNothing(CanvasEraseMode mode)
    {
        var graph = Sized<NodeGraph>();
        var scene = (CanvasScene)graph.Scene;
        scene.Add(new ElementItem(new CanvasNode { Title = "Add" }, new Rect(0, 0, 160, 90)));

        var eraser = new EraseTool(mode);
        eraser.OnPressed(graph, At(40, 40));
        eraser.OnMoved(graph, At(80, 60));
        eraser.OnReleased(graph, At(80, 60));

        Assert.That(scene.Items, Has.Count.EqualTo(1), "an eraser rubbed a node out of a graph");
    }

    // A key TYPED INTO SOMETHING is not the canvas's. Keyboard events bubble, so renaming a node in the inspector sent
    // every letter of the new name through here: the tool changed halfway through the word and the rest of it was
    // typed onto the drawing.
    [Test]
    public void AKeyTypedIntoSomethingElseIsNotTheCanvasS()
    {
        var canvas = Sized<DrawingBoard>();
        var select = new SelectTool { Shortcut = Key.V };
        var pen = new PenTool { Shortcut = Key.P };

        canvas.Tools.Add(select);
        canvas.Tools.Add(pen);
        canvas.Tool = select;

        var typing = new TextBox();
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.P, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            OriginalSource = typing
        };

        canvas.RaiseEvent(args);

        Assert.That(canvas.Tool, Is.SameAs(select), "a letter typed into a field changed the tool in hand");
    }

    // ...and a letter typed ONTO THE PLANE is not the canvas's either. A caret in a word answers only the few keys that
    // mean something to it - the letters themselves arrive as text - so everything else went on to the shortcuts and
    // typing a word picked a tool per letter, throwing the word away.
    [Test]
    public void ALetterTypedIntoTextOnThePlaneDoesNotPickATool()
    {
        var canvas = Sized<DrawingBoard>();
        var pen = new PenTool { Shortcut = Key.P };
        var text = new TextTool { Shortcut = Key.T };

        canvas.Tools.Add(pen);
        canvas.Tools.Add(text);
        canvas.Tool = text;

        // A caret in a fresh piece, which is what a press with the text tool leaves.
        text.OnPressed(canvas, At(10, 10));

        Assert.That(text.IsBusy, Is.True, "there is no caret, so this proves nothing");

        canvas.RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.P, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        });

        Assert.That(canvas.Tool, Is.SameAs(text), "a letter typed into a caption handed over the pen");
    }
}
