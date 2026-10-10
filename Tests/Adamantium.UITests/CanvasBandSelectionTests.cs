using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A selection band takes what it touches of what is DRAWN, not of the box round it: a band inside a large
/// hollow outline takes what it is drawn round and leaves the outline alone.</summary>
public class CanvasBandSelectionTests
{
    private static (InfiniteCanvas canvas, CanvasScene scene) Stage(CanvasMode mode = CanvasMode.Drawing)
    {
        var canvas = new InfiniteCanvas { Mode = mode };
        canvas.Measure(new Size(800, 600), force: true);
        canvas.Arrange(new Rect(0, 0, 800, 600));

        var scene = new CanvasScene();
        canvas.Scene = scene;

        return (canvas, scene);
    }

    private static List<ICanvasItem> Band(InfiniteCanvas canvas, double x0, double y0, double x1, double y1)
    {
        var tool = new SelectTool();
        canvas.Tools.Add(tool);
        canvas.Tool = tool;

        tool.OnPressed(canvas, At(x0, y0));
        tool.OnMoved(canvas, At(x1, y1));
        tool.OnReleased(canvas, At(x1, y1));

        return canvas.Selection.ToList();
    }

    private static CanvasPointerEventArgs At(double x, double y)
    {
        var p = new Vector2(x, y);
        return new CanvasPointerEventArgs { World = p, Pointer = p, Screen = p, Button = MouseButtons.Left, ClickCount = 1 };
    }

    private static ShapeItem Outline(CanvasShape shape, Brush fill = null) =>
        new(shape, new Rect(0, 0, 400, 300), Brushes.White, 2, fill);

    private static ShapeItem Box(double x, double y) =>
        new(CanvasShape.Rectangle, new Rect(x, y, 50, 50), Brushes.White, 2, Brushes.Red);

    [TestCase(CanvasShape.Rectangle)]
    [TestCase(CanvasShape.Ellipse)]
    [TestCase(CanvasShape.Polygon)]
    public void ABandInsideAHollowOutline_TakesWhatItIsDrawnRound_NotTheOutline(CanvasShape shape)
    {
        var (canvas, scene) = Stage();
        var outline = Outline(shape);
        var inner = Box(175, 140);
        scene.Add(outline);
        scene.Add(inner);

        Assert.That(Band(canvas, 165, 130, 235, 200), Is.EqualTo(new List<ICanvasItem> { inner }));
    }

    [Test]
    public void ABandAcrossTheOutline_TakesIt()
    {
        var (canvas, scene) = Stage();
        var outline = Outline(CanvasShape.Rectangle);
        var inner = Box(20, 120);
        scene.Add(outline);
        scene.Add(inner);

        Assert.That(Band(canvas, -10, 110, 80, 180), Is.EquivalentTo(new ICanvasItem[] { outline, inner }));
    }

    // Asked of the shape directly: a press inside a filled shape grabs it, so no band can start there.
    [TestCase(CanvasShape.Rectangle)]
    [TestCase(CanvasShape.Ellipse)]
    [TestCase(CanvasShape.Polygon)]
    public void AFilledShape_IsTouchedByARectangleInsideIt(CanvasShape shape)
    {
        var filled = Outline(shape, Brushes.Blue);

        Assert.That(filled.Touches(new Rect(180, 130, 40, 40)), Is.True);
    }

    [Test]
    public void AnEllipse_IsNotTakenByABandInTheCornerOfItsBox()
    {
        var (canvas, scene) = Stage();
        var ellipse = Outline(CanvasShape.Ellipse, Brushes.Blue);
        scene.Add(ellipse);

        Assert.Multiple(() =>
        {
            Assert.That(Band(canvas, 2, 2, 30, 30), Is.Empty, "the corner of the box is outside the ellipse");
            Assert.That(Band(canvas, 410, 140, 390, 160), Is.EqualTo(new List<ICanvasItem> { ellipse }));
        });
    }

    [Test]
    public void ATurnedHollowOutline_IsNotTakenByABandInItsMiddle()
    {
        var (canvas, scene) = Stage();
        var outline = Outline(CanvasShape.Rectangle);
        outline.Angle = 30;
        scene.Add(outline);

        Assert.That(Band(canvas, 180, 130, 220, 170), Is.Empty);
    }

    [Test]
    public void AFreehandLoop_IsTakenOnlyWhereItsInkIs()
    {
        var (canvas, scene) = Stage();
        var loop = new StrokeItem(new Vector2(0, 0), Brushes.White, 2);
        loop.Add(new Vector2(0, 0));
        loop.Add(new Vector2(300, 0));
        loop.Add(new Vector2(300, 300));
        loop.Add(new Vector2(0, 300));
        loop.Add(new Vector2(0, 0));
        scene.Add(loop);

        Assert.Multiple(() =>
        {
            Assert.That(Band(canvas, 100, 100, 200, 200), Is.Empty, "a band inside the loop touches none of its ink");
            Assert.That(Band(canvas, -10, 140, 10, 160), Is.EqualTo(new List<ICanvasItem> { loop }));
        });
    }

    // The rule a band always had: it takes what it TOUCHES, so a long stroke is taken by a band round part of it.
    [Test]
    public void ALongStroke_IsTakenByABandRoundPartOfIt()
    {
        var (canvas, scene) = Stage();
        var line = new ShapeItem(CanvasShape.Line, new Rect(0, 100, 600, 0), Brushes.White, 2);
        scene.Add(line);

        Assert.That(Band(canvas, 50, 80, 120, 120), Is.EqualTo(new List<ICanvasItem> { line }));
    }

    [Test]
    public void ADiagonalLine_IsTakenOnlyWhereItRuns_NotInTheEmptyCornerOfItsBox()
    {
        var (canvas, scene) = Stage();
        var line = new ShapeItem(CanvasShape.Line, new Rect(0, 0, 300, 300), Brushes.White, 2);
        scene.Add(line);

        Assert.Multiple(() =>
        {
            Assert.That(Band(canvas, 250, 10, 290, 50), Is.Empty, "the far corner of its box has nothing of the line");
            Assert.That(Band(canvas, 130, 170, 170, 130), Is.EqualTo(new List<ICanvasItem> { line }));
        });
    }

    // Where the head is wider than the line, a band touching only the head still takes the arrow.
    [Test]
    public void AnArrow_IsTakenByABandTouchingOnlyItsHead()
    {
        var (canvas, scene) = Stage();
        var arrow = new ShapeItem(CanvasShape.Arrow, new Rect(0, 100, 300, 0), Brushes.White, 10);
        scene.Add(arrow);

        Assert.That(Band(canvas, 275, 86, 285, 93), Is.EqualTo(new List<ICanvasItem> { arrow }));
    }

    [Test]
    public void AGroup_IsTakenByWhatItsChildrenDraw_NotByItsBox()
    {
        var (canvas, scene) = Stage();
        var group = new GroupItem(new ICanvasItem[] { Box(0, 0), Box(350, 250) });
        scene.Add(group);

        Assert.Multiple(() =>
        {
            Assert.That(Band(canvas, 150, 120, 250, 180), Is.Empty, "the middle of the group's box is empty");
            Assert.That(Band(canvas, 340, 240, 420, 320), Is.EqualTo(new List<ICanvasItem> { group }));
        });
    }

    // Asked of the shape directly: a press on the outline grabs it, so no band can start there.
    [Test]
    public void AHollowPolygon_IsTouchedInsideTheWidthOfItsOutline()
    {
        var polygon = new ShapeItem(CanvasShape.Polygon, new Rect(0, 0, 400, 300), Brushes.White, 20);

        Assert.Multiple(() =>
        {
            Assert.That(polygon.Touches(new Rect(150, 285, 100, 10)), Is.True, "the band lies on the outline's width");
            Assert.That(polygon.Touches(new Rect(180, 130, 40, 40)), Is.False, "the band lies in the hole");
        });
    }

    [Test]
    public void ABandRoundANodeOnACommentFrame_TakesTheNode_NotTheFrame()
    {
        var (canvas, scene) = Stage(CanvasMode.Nodes);
        var frame = new CanvasFrameItem(new Rect(0, 0, 500, 400), "Notes", Brushes.White, Brushes.Gray);
        var node = new ElementItem(new CanvasNode { Title = "Multiply" }, new Rect(150, 150, 160, 90));
        scene.Add(frame);
        scene.Add(node);

        Assert.That(Band(canvas, 140, 140, 320, 250), Is.EqualTo(new List<ICanvasItem> { node }));
    }
}
