using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What the plane's own content offers the select tool (<see cref="ICanvasContentGesture"/>): asked where no
/// item is, after the items and before a band, it takes the press, its moves and its release; where it offers nothing
/// the band opens as before; the pointer wears its cursor where no item is.</summary>
public class CanvasContentGestureTests
{
    private sealed class ContentBoard : DrawingBoard
    {
        public ContentBoard(ICanvasContentGesture gesture)
        {
            ContentGesture = gesture;
        }
    }

    private sealed class Content : ICanvasContentGesture
    {
        public Rect Area { get; init; }

        public int Pressed { get; private set; }

        public int Moved { get; private set; }

        public int Released { get; private set; }

        public bool IsBusy { get; private set; }

        public Cursor CursorAt(InfiniteCanvas canvas, Vector2<double> world) => Area.Contains(world) ? Cursors.IBeam : null;

        public bool Press(InfiniteCanvas canvas, CanvasPointerEventArgs e)
        {
            if (!Area.Contains(e.World))
            {
                return false;
            }

            Pressed++;
            IsBusy = true;
            return true;
        }

        public void Move(InfiniteCanvas canvas, CanvasPointerEventArgs e) => Moved++;

        public void Release(InfiniteCanvas canvas, CanvasPointerEventArgs e)
        {
            Released++;
            IsBusy = false;
        }

        public void Cancel(InfiniteCanvas canvas) => IsBusy = false;
    }

    private static CanvasPointerEventArgs At(double x, double y)
    {
        var point = new Vector2<double>(x, y);
        return new CanvasPointerEventArgs { World = point, Pointer = point, Screen = point, Button = MouseButtons.Left, ClickCount = 1 };
    }

    private static ContentBoard Stage(Content content, out SelectTool tool, out ShapeItem item)
    {
        var canvas = new ContentBoard(content);
        canvas.Measure(new Size<double>(800, 600), force: true);
        canvas.Arrange(new Rect(0, 0, 800, 600));
        var scene = new CanvasScene();
        item = new ShapeItem(CanvasShape.Rectangle, new Rect(50, 50, 40, 40), Brushes.White, 2, Brushes.White);
        scene.Add(item);
        canvas.Scene = scene;
        tool = new SelectTool();
        canvas.Tools.Add(tool);
        canvas.Tool = tool;
        return canvas;
    }

    [Test]
    public void APressOnTheContent_IsTheContents_ItsMovesAndReleaseToo()
    {
        var content = new Content { Area = new Rect(300, 300, 200, 100) };
        var canvas = Stage(content, out var tool, out _);

        tool.OnPressed(canvas, At(350, 350));
        var busy = tool.IsBusy;
        tool.OnMoved(canvas, At(450, 380));
        tool.OnReleased(canvas, At(450, 380));

        Assert.Multiple(() =>
        {
            Assert.That(content.Pressed, Is.EqualTo(1));
            Assert.That(busy, Is.True, "the tool does not say a press of its content is held");
            Assert.That(content.Moved, Is.EqualTo(1));
            Assert.That(content.Released, Is.EqualTo(1));
            Assert.That(tool.IsBusy, Is.False);
            Assert.That(canvas.Selection, Is.Empty, "a band opened over the content");
        });
    }

    [Test]
    public void AnItem_TakesThePressFirst_AndWhereTheContentOffersNothing_ABandOpens()
    {
        var content = new Content { Area = new Rect(0, 0, 800, 600) };
        var canvas = Stage(content, out var tool, out var item);

        tool.OnPressed(canvas, At(70, 70));
        tool.OnReleased(canvas, At(70, 70));
        var picked = canvas.Selection.ToList();
        content = new Content { Area = new Rect(600, 500, 10, 10) };
        canvas = Stage(content, out tool, out item);
        tool.OnPressed(canvas, At(20, 20));
        tool.OnMoved(canvas, At(120, 120));
        tool.OnReleased(canvas, At(120, 120));

        Assert.Multiple(() =>
        {
            Assert.That(picked, Has.Count.EqualTo(1), "the content took a press on an item");
            Assert.That(content.Pressed, Is.Zero);
            Assert.That(canvas.Selection, Does.Contain(item), "no band opened where the content offers nothing");
        });
    }

    [Test]
    public void ThePointer_WearsTheContentsCursor_WhereNoItemIs()
    {
        var content = new Content { Area = new Rect(0, 0, 800, 600) };
        var canvas = Stage(content, out _, out _);

        canvas.ShowPointerAt(canvas.WorldToScreen(new Vector2<double>(400, 300)));
        var overContent = canvas.Cursor;
        canvas.ShowPointerAt(canvas.WorldToScreen(new Vector2<double>(70, 70)));

        Assert.Multiple(() =>
        {
            Assert.That(overContent, Is.EqualTo(Cursors.IBeam));
            Assert.That(canvas.Cursor, Is.Not.EqualTo(Cursors.IBeam), "the content's cursor over an item");
        });
    }
}
