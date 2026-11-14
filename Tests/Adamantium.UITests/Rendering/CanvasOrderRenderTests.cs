using System.Runtime.InteropServices;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>WHICH OF TWO OVERLAPPING THINGS IS ON TOP, asked of the pixels. The scene's order reaches the layers - a
/// plain test can show that - but what a layer hands to the frame is sorted again on the way to the card, into batches
/// by KIND, and a batch is one place in the frame. So the order a layer asks for and the order that comes out are two
/// different questions, and only this one can answer the second.</summary>
[TestFixture]
[Category("Gpu")]
public class CanvasOrderRenderTests
{
    private const int Dim = 200;

    private static OffscreenTestRenderer _renderer;

    [OneTimeSetUp]
    public void CreateRenderer()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
            Dim, Dim)
        {
            ClearColor = Colors.Black
        };
    }

    [OneTimeTearDown]
    public void DisposeRenderer()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    private static ControlTemplate Template() => new(() =>
    {
        var layers = new Grid();
        var front = new CanvasFrontLayer();
        var root = new Grid();

        root.Children.Add(layers);
        root.Children.Add(front);

        var result = new TemplateResult { RootComponent = root };

        result.RegisterName("PART_Layers", layers);
        result.RegisterName("PART_Front", front);
        return result;
    });

    private static InfiniteCanvas Canvas()
    {
        var canvas = new DrawingBoard
        {
            Template = Template(),
            Scene = new CanvasScene(),
            Ink = Brushes.Black,
            Width = Dim,
            Height = Dim,
            // NO GROUND: the grid is one quad with a shader on it over the whole viewport, and what is being asked
            // here is which of two things is on top of the other.
            GridStyle = CanvasGridStyle.Transparent
        };

        return canvas;
    }

    private static (byte R, byte G, byte B) Middle(InfiniteCanvas canvas)
    {
        var pixels = Pixels(canvas);

        // The middle of the canvas, which is where the world's origin sits and where both shapes are put.
        var i = (Dim / 2 * Dim + Dim / 2) * 4;

        return (pixels[i + 2], pixels[i + 1], pixels[i + 0]);
    }

    private static ShapeItem Box(Color color) =>
        new(CanvasShape.Rectangle, new Rect(-40, -40, 80, 80), Brushes.Transparent, 0, new SolidColorBrush(color));

    // TWO OF THE SAME SORT. Both are rectangles, so both go to the same batch and their order inside it is the only
    // thing deciding - the easy case, and the one that has to work before anything else is asked.
    [Test]
    public void TheLaterOfTwoShapesIsOnTop()
    {
        var canvas = Canvas();
        var first = Box(Colors.Red);
        var second = Box(Colors.Lime);

        canvas.Scene.Add(first);
        canvas.Scene.Add(second);

        var over = Middle(canvas);

        Assert.That(over.G, Is.GreaterThan(200), $"the one added last is not on top: {over}");

        canvas.Scene.BringToFront(first);

        var swapped = Middle(canvas);

        Assert.That(swapped.R, Is.GreaterThan(200),
            $"bringing a shape to the front did not put it over another shape: {swapped}");
    }

    // TWO STROKES, which is the case a person actually hits: ink is drawn by a pass of its own, and a pass that laid
    // its strokes down in the order it happened to collect them would ignore the plane's order entirely.
    [Test]
    public void TheLaterOfTwoStrokesIsOnTop()
    {
        var canvas = Canvas();
        var first = Ink(Colors.Red);
        var second = Ink(Colors.Lime);

        canvas.Scene.Add(first);
        canvas.Scene.Add(second);

        var over = Middle(canvas);

        Assert.That(over.G, Is.GreaterThan(120), $"the stroke drawn last is not on top: {over}");

        canvas.Scene.BringToFront(first);

        var swapped = Middle(canvas);

        Assert.That(swapped.R, Is.GreaterThan(120),
            $"bringing a stroke to the front did not put it over another stroke: {swapped}");
    }

    private static StrokeItem Ink(Color color)
    {
        var ink = new StrokeItem(new Vector2<double>(-40, 0), new SolidColorBrush(color), 40);

        ink.Add(new Vector2<double>(-40, 0));
        ink.Add(new Vector2<double>(40, 0));

        return ink;
    }

    // A POLYGON is drawn by a pass of ITS OWN - neither the rounded-rect batch nor the ink one - so whether it obeys
    // the plane's order is a separate question from every other shape's, and has to be asked separately.
    [Test]
    public void TheLaterOfTwoPolygonsIsOnTop()
    {
        var canvas = Canvas();
        var first = Hexagon(Colors.Red);
        var second = Hexagon(Colors.Lime);

        canvas.Scene.Add(first);
        canvas.Scene.Add(second);

        var over = Middle(canvas);

        Assert.That(over.G, Is.GreaterThan(120), $"the polygon added last is not on top: {over}");

        canvas.Scene.BringToFront(first);

        var swapped = Middle(canvas);

        Assert.That(swapped.R, Is.GreaterThan(120),
            $"bringing a polygon to the front did not put it over another polygon: {swapped}");
    }

    [Test]
    public void APolygonAndARectangleObeyTheSameOrder()
    {
        var canvas = Canvas();
        var box = Box(Colors.Red);
        var polygon = Hexagon(Colors.Lime);

        canvas.Scene.Add(box);
        canvas.Scene.Add(polygon);

        var over = Middle(canvas);

        Assert.That(over.G, Is.GreaterThan(120), $"a polygon drawn after a rectangle went under it: {over}");

        canvas.Scene.BringToFront(box);

        var swapped = Middle(canvas);

        Assert.That(swapped.R, Is.GreaterThan(120),
            $"a rectangle brought to the front stayed under the polygon: {swapped}");
    }

    private static ShapeItem Hexagon(Color color) =>
        new(CanvasShape.Polygon, new Rect(-40, -40, 80, 80), Brushes.Transparent, 0, new SolidColorBrush(color))
        {
            Sides = 6
        };

    // TWO OF DIFFERENT SORTS - a stroke and a shape. They go to DIFFERENT batches, and a batch is one place in the
    // frame, so this is the question the scene's order cannot answer on its own.
    [Test]
    public void AStrokeAndAShapeObeyTheSameOrder()
    {
        var canvas = Canvas();
        var box = Box(Colors.Red);
        var ink = new StrokeItem(new Vector2<double>(-40, 0), new SolidColorBrush(Colors.Lime), 30);

        ink.Add(new Vector2<double>(-40, 0));
        ink.Add(new Vector2<double>(40, 0));

        canvas.Scene.Add(box);
        canvas.Scene.Add(ink);

        var over = Middle(canvas);

        Assert.That(over.G, Is.GreaterThan(120), $"a stroke drawn after a shape went under it: {over}");

        canvas.Scene.BringToFront(box);

        var swapped = Middle(canvas);

        Assert.That(swapped.R, Is.GreaterThan(120),
            $"a shape brought to the front stayed under the stroke: {swapped}");
    }

    // A CONTROL AND A SHAPE - the pair the whole one-order design is for, and the hardest: a control is a real child of
    // a layer and is drawn as itself, while a shape goes through a batch that is flushed by its own kind. Nothing about
    // the two is drawn by the same machinery, so nothing but the pixels can say which came out on top.
    [Test]
    public void AControlAndAShapeObeyTheSameOrder()
    {
        var canvas = Canvas();
        var box = Box(Colors.Red);
        var picture = new Adamantium.UI.Controls.Image
        {
            Background = new SolidColorBrush(Colors.Lime),
            Width = 80,
            Height = 80
        };
        var element = new ElementItem(picture, new Rect(-40, -40, 80, 80));

        canvas.Scene.Add(box);
        canvas.Scene.Add(element);

        var over = Middle(canvas);

        Assert.That(over.G, Is.GreaterThan(120), $"a picture placed after a shape went under it: {over}");

        canvas.Scene.BringToFront(box);

        var swapped = Middle(canvas);

        Assert.That(swapped.R, Is.GreaterThan(120),
            $"a shape brought to the front stayed under the picture: {swapped}");

        canvas.Scene.BringToFront(element);

        var back = Middle(canvas);

        Assert.That(back.G, Is.GreaterThan(120),
            $"a picture brought to the front stayed under the shape: {back}");
    }

    // A PICTURE GATHERED INTO A GROUP IS STILL DRAWN. Gathering moves a control from one layer to another - the runs
    // are cut afresh and the layer it was in may not even exist any more - and what is being asked here is whether the
    // frame knows: the control can be in the tree, visible and the right size, and still not be recorded.
    [Test]
    public void APictureGatheredIntoAGroupIsStillDrawn()
    {
        var canvas = Canvas();
        var picture = new Adamantium.UI.Controls.Image
        {
            Background = new SolidColorBrush(Colors.Lime),
            Width = 80,
            Height = 80
        };
        var element = new ElementItem(picture, new Rect(-40, -40, 80, 80));

        var ink = new StrokeItem(new Vector2<double>(-40, 0), new SolidColorBrush(Colors.Red), 4);

        ink.Add(new Vector2<double>(-40, 0));
        ink.Add(new Vector2<double>(-30, 0));

        canvas.Scene.Add(element);
        canvas.Scene.Add(ink);

        Assert.That(Middle(canvas).G, Is.GreaterThan(120), "the picture was not drawn before it was gathered");

        canvas.SelectMany(new System.Collections.Generic.List<ICanvasItem> { element, ink }, false);
        canvas.GroupSelection();

        var after = Middle(canvas);

        Assert.That(after.G, Is.GreaterThan(120), $"the picture went dark the moment it was gathered: {after}");
    }

    // ...AND OUT OF A RUN IT SHARED WITH ANOTHER CONTROL, which is the shape a real plane has. The case above always
    // rebuilds the layer it moves, because the sort at that place changes; here the run below KEEPS its layer and the
    // control is carried out of it into a new one - so what is being asked is whether a control that changed layers
    // takes its drawing with it.
    [Test]
    public void AControlCarriedOutOfItsRunIsDrawnWhereItLands()
    {
        var canvas = Canvas();
        var beside = new ElementItem(
            new Adamantium.UI.Controls.Image { Background = new SolidColorBrush(Colors.Blue), Width = 40, Height = 40 },
            new Rect(-90, -90, 40, 40));
        var picture = new Adamantium.UI.Controls.Image
        {
            Background = new SolidColorBrush(Colors.Lime),
            Width = 80,
            Height = 80
        };
        var moved = new ElementItem(picture, new Rect(-40, -40, 80, 80));

        // TWO CONTROLS FIRST, so they are one run and one layer, and then the shape over both of them.
        canvas.Scene.Add(beside);
        canvas.Scene.Add(moved);
        canvas.Scene.Add(Box(Colors.Red));

        var under = Middle(canvas);

        Assert.That(under.R, Is.GreaterThan(120), $"the shape put on top is not on top: {under}");

        canvas.Scene.BringToFront(moved);

        var over = Middle(canvas);

        Assert.That(over.G, Is.GreaterThan(120),
            $"a control carried out of its run into a new layer is still drawn where it was: {over}");
    }

    // The same check on a second, patched frame, the path a running canvas takes (the tests above use fresh roots).
    [Test]
    public void APictureGatheredIntoAGroupSurvivesTheNextFrame()
    {
        var canvas = Canvas();
        var picture = new Adamantium.UI.Controls.Image
        {
            Background = new SolidColorBrush(Colors.Lime),
            Width = 80,
            Height = 80
        };
        var element = new ElementItem(picture, new Rect(-40, -40, 80, 80));

        var ink = new StrokeItem(new Vector2<double>(-40, 0), new SolidColorBrush(Colors.Red), 4);

        ink.Add(new Vector2<double>(-40, 0));
        ink.Add(new Vector2<double>(-30, 0));

        canvas.Scene.Add(element);
        canvas.Scene.Add(ink);

        // ONE ROOT for both frames - that is the whole point of this test.
        Lay(canvas);

        var root = new VisualRoot(canvas, Dim, Dim);

        Assert.That(Frame(root).G, Is.GreaterThan(120), "the picture was not drawn before it was gathered");

        canvas.SelectMany(new System.Collections.Generic.List<ICanvasItem> { element, ink }, false);
        canvas.GroupSelection();

        Lay(canvas);

        var after = Frame(root);

        Assert.That(after.G, Is.GreaterThan(120),
            $"the picture went dark in the frame after it was gathered: {after}");
    }

    // AN ELLIPSE AND A POLYGON are two batches that the order check counted as one layer, so neither asked about the
    // other: the polygon, flushed after the ellipse, covered an ellipse drawn after it.
    [Test]
    public void AnEllipseDrawnAfterAPolygonIsOnTop()
    {
        var canvas = Canvas();

        canvas.Scene.Add(Hexagon(Colors.Red));
        canvas.Scene.Add(new ShapeItem(CanvasShape.Ellipse, new Rect(-40, -40, 80, 80), Brushes.Transparent, 0,
            new SolidColorBrush(Colors.Lime)));

        var over = Middle(canvas);

        Assert.That(over.G, Is.GreaterThan(120), $"an ellipse drawn after a polygon went under it: {over}");
    }

    // INK AND A LINE, the same: two batches counted as one layer.
    [Test]
    public void InkDrawnAfterALineIsOnTop()
    {
        var canvas = Canvas();

        canvas.Scene.Add(new ShapeItem(CanvasShape.Line, new Rect(-40, -40, 80, 80), new SolidColorBrush(Colors.Red), 30,
            Brushes.Transparent));
        canvas.Scene.Add(Ink(Colors.Lime));

        var over = Middle(canvas);

        Assert.That(over.G, Is.GreaterThan(120), $"ink drawn after a line went under it: {over}");
    }

    // TEXT is flushed after every fill and stroke, so whatever is drawn over a label later has to flush it first.
    [Test]
    public void InkDrawnOverTextIsOnTop() => DrawnOverTextIsOnTop(Ink(Colors.Lime), "ink");

    [Test]
    public void AFilledPathDrawnOverTextIsOnTop() =>
        DrawnOverTextIsOnTop(new PathItem("M -60 -60 L 60 -60 L 60 60 L -60 60 Z", new SolidColorBrush(Colors.Lime),
            Brushes.Transparent, 0), "a filled path");

    private static void DrawnOverTextIsOnTop(ICanvasItem over, string what)
    {
        var canvas = Canvas();

        canvas.Scene.Add(new TextItem(new Vector2<double>(-40, -30), "HH", new SolidColorBrush(Colors.Red), 48));

        var glyph = RedNearTheMiddleRow(Pixels(canvas));
        Assert.That(glyph, Is.GreaterThanOrEqualTo(0), "the text drew nothing where the cover goes");

        canvas.Scene.Add(over);

        var pixels = Pixels(canvas);
        var red = pixels[glyph + 2];
        var green = pixels[glyph + 1];

        Assert.That(green > 120 && red < 120, Is.True, $"{what} drawn over text went under it: red {red}, green {green}");
    }

    private static byte[] Pixels(InfiniteCanvas canvas)
    {
        Lay(canvas);

        Assert.That(_renderer.RenderFrame(new VisualRoot(canvas, Dim, Dim)), Is.True);

        using var img = _renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];

        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);
        return pixels;
    }

    private static int RedNearTheMiddleRow(byte[] pixels)
    {
        const int reach = 10;
        for (var row = Dim / 2 - reach; row <= Dim / 2 + reach; row++)
        {
            for (var column = 0; column < Dim; column++)
            {
                var at = (row * Dim + column) * 4;
                if (pixels[at + 2] > 200 && pixels[at + 1] < 60) return at;
            }
        }

        return -1;
    }

    private static void Lay(InfiniteCanvas canvas)
    {
        // Twice, for the reason given in Middle: the stack is filled by the first pass and placed by the second.
        for (var pass = 0; pass < 2; pass++)
        {
            canvas.Measure(new Size<double>(Dim, Dim));
            canvas.Arrange(new Rect(0, 0, Dim, Dim));
        }
    }

    private static (byte R, byte G, byte B) Frame(VisualRoot root)
    {
        Assert.That(_renderer.RenderFrame(root), Is.True);

        // What a real frame does when it is finished. Left standing, the marks of this frame would make the next one
        // look like a frame that had been asked for again.
        RenderDirty.Clear();

        using var img = _renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];

        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);

        var i = (Dim / 2 * Dim + Dim / 2) * 4;

        return (pixels[i + 2], pixels[i + 1], pixels[i + 0]);
    }
}
