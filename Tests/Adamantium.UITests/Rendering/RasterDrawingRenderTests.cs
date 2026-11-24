using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// An element drawn as a raster (DrawingMode.Raster) shows what it shows drawn from its shapes, drawn once into tiles and
// laid down from them after: a frame that changes nothing draws no tile, one that changes what it draws draws them anew.
[TestFixture]
[Category("Gpu")]
public class RasterDrawingRenderTests
{
    private const int Dim = 160;

    private sealed class Picture : MeasurableUIComponent
    {
        public Color Paint = Colors.Red;

        // A group cut by a path, drawn apart into targets of its own - inside the tile's.
        public bool Cut;

        protected override Size<double> MeasureOverride(Size<double> availableSize) => new(Dim, Dim);

        protected override void OnRender(IDrawingContext context)
        {
            var session = context.ForControl(this);
            var ring = new StreamGeometry { FillRule = FillRule.EvenOdd };
            var figures = ring.Open();
            figures.BeginFigure(new Vector2<double>(20, 20), true, true).LineTo(new Vector2<double>(140, 30))
                .LineTo(new Vector2<double>(120, 140)).LineTo(new Vector2<double>(30, 120)).CloseFigure();
            figures.BeginFigure(new Vector2<double>(60, 60), true, true).LineTo(new Vector2<double>(100, 62))
                .LineTo(new Vector2<double>(95, 100)).LineTo(new Vector2<double>(58, 96)).CloseFigure();
            ring.RecalculateBounds();
            if (Cut)
            {
                var triangle = new StreamGeometry { FillRule = FillRule.NonZero };
                triangle.Open().BeginFigure(new Vector2<double>(0, 0), true, true).LineTo(new Vector2<double>(Dim, 0))
                    .LineTo(new Vector2<double>(0, Dim)).CloseFigure();
                triangle.RecalculateBounds();
                session.PushGroup(new DrawingGroupOptions { Clip = triangle });
            }

            session.DrawGeometry(new SolidColorBrush(Paint), ring, new Pen(new SolidColorBrush(Colors.Blue), 3),
                Matrix4x4F.Identity);
            if (Cut)
            {
                session.PopGroup();
            }

            session.DrawRectangle(new SolidColorBrush(Colors.Green), new Rect(110, 110, 40, 40));
        }
    }

    private sealed class Frame(byte[] pixels, int tilesDrawn)
    {
        public int TilesDrawn { get; } = tilesDrawn;

        public (int R, int G, int B) At(int x, int y)
        {
            var at = (y * Dim + x) * 4;
            return (pixels[at + 2], pixels[at + 1], pixels[at]);
        }
    }

    private sealed class Stand : IDisposable
    {
        private readonly OffscreenTestRenderer _renderer;
        private readonly VisualRoot _root;

        public Stand(UIComponent shown)
        {
            var device = GpuTestDevice.Device;
            var factory = new RenderUnitFactory(device, new DeviceResourceFactory(device));
            _renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.White };
            _root = new VisualRoot(shown, Dim, Dim);
        }

        public Frame Render()
        {
            ((IMeasurableComponent)_root).Measure(new Size<double>(Dim, Dim));
            ((IMeasurableComponent)_root).Arrange(new Rect(0, 0, Dim, Dim));
            Assert.That(_renderer.RenderFrame(_root), Is.True, "off-screen frame must render");
            var drawn = _renderer.Cache.LayersRedrawn;
            using var image = _renderer.RenderTarget.ResolveTexture.ToImage();
            var bytes = new byte[(int)image.TotalSizeInBytes];
            Marshal.Copy(image.DataPointer, bytes, 0, bytes.Length);
            return new Frame(bytes, drawn);
        }

        public RenderCache Cache => _renderer.Cache;

        public void Dispose() => _renderer.Dispose();
    }

    // The same picture, edges softened at most: a tile drawn at the next power of two and laid down smaller softens every
    // edge by a pixel, so how far apart two frames are is told by the mean over the picture and by the pixels more than
    // half apart - not by every edge pixel that moved a little.
    private static void AssertTheSame(Frame drawn, Frame wanted, string what)
    {
        var sum = 0.0;
        var far = 0;
        for (var y = 0; y < Dim; y++)
        {
            for (var x = 0; x < Dim; x++)
            {
                var (r, g, b) = drawn.At(x, y);
                var (wr, wg, wb) = wanted.At(x, y);
                var apart = (Math.Abs(r - wr) + Math.Abs(g - wg) + Math.Abs(b - wb)) / 3.0;
                sum += apart;
                if (apart > 128)
                {
                    far++;
                }
            }
        }

        Assert.That(sum / (Dim * Dim), Is.LessThanOrEqualTo(2.0), $"{what}: mean apart, of 255");
        Assert.That(far, Is.LessThanOrEqualTo(Dim / 8), $"{what}: pixels more than half apart");
    }

    private static Frame Once(DrawingMode mode, double scale = 1, double moveX = 0, double moveY = 0, bool cut = false)
    {
        var picture = new Picture { DrawingMode = mode, Cut = cut };
        var host = new Canvas
        {
            Width = Dim, Height = Dim,
            RenderTransform = new Transform { ScaleX = scale, ScaleY = scale, TranslateX = moveX, TranslateY = moveY }
        };
        host.Children.Add(picture);
        using var stand = new Stand(host);
        return stand.Render();
    }

    [TestCase(1.0, 0.0, 0.0)]
    [TestCase(0.75, 10.0, 6.0)]
    [TestCase(1.6, -40.0, -30.0)]
    public void ARaster_ShowsWhatItsShapesShow(double scale, double moveX, double moveY)
    {
        var vector = Once(DrawingMode.Vector, scale, moveX, moveY);
        var raster = Once(DrawingMode.Raster, scale, moveX, moveY);

        Assert.Multiple(() =>
        {
            Assert.That(raster.TilesDrawn, Is.GreaterThan(0), "drawn into tiles");
            AssertTheSame(raster, vector, "against its shapes' picture");
        });
    }

    private static Frame Turned(DrawingMode mode, Transform transform)
    {
        var host = new Canvas
        {
            Width = Dim, Height = Dim, RenderTransform = transform, RenderTransformOrigin = new Vector2<double>(0.5, 0.5)
        };
        host.Children.Add(new Picture { DrawingMode = mode });
        using var stand = new Stand(host);
        return stand.Render();
    }

    // Mirrored, or turned a quarter, it shows what its shapes show: not laid down the right way round.
    [TestCase(-1.0, 1.0, 0.0)]
    [TestCase(1.0, -1.0, 0.0)]
    [TestCase(1.0, 1.0, 90.0)]
    public void AMirroredOrTurnedRaster_ShowsWhatItsShapesShow(double scaleX, double scaleY, double angle)
    {
        Transform Make() => new() { ScaleX = scaleX, ScaleY = scaleY, RotationAngle = angle };

        AssertTheSame(Turned(DrawingMode.Raster, Make()), Turned(DrawingMode.Vector, Make()), "against its shapes' picture");
    }

    // What never holds still - a living aura - is drawn anew to the last square again and again, not begun anew each
    // frame and never finished.
    [Test]
    public void WhatNeverHoldsStill_IsDrawnAnewToTheLastSquare()
    {
        var host = new Canvas
        {
            Width = Dim, Height = Dim, DrawingMode = DrawingMode.Raster, RasterTileSize = 64,
            RasterFrameBudget = TimeSpan.Zero
        };
        var part = new Square(Colors.White)
        {
            Aura = new Aura { Radius = 12, Turbulence = 1, Flow = 1, Color = Colors.Red }
        };
        Canvas.SetLeft(part, 60);
        Canvas.SetTop(part, 60);
        host.Children.Add(part);
        using var stand = new Stand(host);
        stand.Render();
        var first = stand.Cache.RasterTexturesFinished;
        for (var i = 0; i < 20; i++)
        {
            stand.Render();
        }

        Assert.That(stand.Cache.RasterTexturesFinished, Is.GreaterThan(first), "textures drawn to the last square");
    }

    // The camera coming back to what a raster holds before it is drawn anew to the last square: what was being drawn
    // goes, and its draws are no longer made ready each frame.
    [Test]
    public void TheCameraComingBack_BeforeTheLastSquare_LeavesNothingBeingDrawn()
    {
        var picture = new Picture { DrawingMode = DrawingMode.Raster, RasterTileSize = 64, RasterFrameBudget = TimeSpan.Zero };
        var host = new Canvas
        {
            Width = Dim, Height = Dim, IsRenderMotionNode = true, RenderTransform = new Transform { ScaleX = 0.6, ScaleY = 0.6 }
        };
        host.Children.Add(picture);
        using var stand = new Stand(host);
        stand.Render();
        host.RenderTransform = new Transform { ScaleX = 1.3, ScaleY = 1.3 };
        stand.Render();
        System.Threading.Thread.Sleep(250);
        stand.Render();
        stand.Render();
        var drawing = stand.Cache.RastersDrawingAnew;
        host.RenderTransform = new Transform { ScaleX = 0.6, ScaleY = 0.6 };
        stand.Render();
        stand.Render();

        Assert.Multiple(() =>
        {
            Assert.That(drawing, Is.EqualTo(1), "being drawn anew at the larger scale");
            Assert.That(stand.Cache.RastersDrawingAnew, Is.EqualTo(0), "back to what it holds: nothing being drawn");
        });
    }

    // A container drawing nothing of its own is drawn as a raster too.
    [Test]
    public void AContainerDrawingNothingOfItsOwn_IsDrawnAsARaster()
    {
        var host = new Canvas { Width = Dim, Height = Dim, DrawingMode = DrawingMode.Raster };
        var square = new Square(Colors.Blue);
        Canvas.SetLeft(square, 100);
        host.Children.Add(square);
        using var stand = new Stand(host);
        var first = stand.Render();

        Assert.That(first.TilesDrawn, Is.GreaterThan(0), "drawn into its texture");
    }

    // Faded from above after its texture is drawn, it shows faded.
    [Test]
    public void FadedFromAboveAfterItIsDrawn_ItShowsFaded()
    {
        Frame Faded(DrawingMode mode)
        {
            var host = new Canvas { Width = Dim, Height = Dim };
            host.Children.Add(new Picture { DrawingMode = mode });
            using var stand = new Stand(host);
            stand.Render();
            host.Opacity = 0.3;
            return stand.Render();
        }

        AssertTheSame(Faded(DrawingMode.Raster), Faded(DrawingMode.Vector), "against its shapes' picture");
    }

    // A part of it faded after its texture is drawn shows faded: what fades inside it is drawn anew.
    [Test]
    public void APartOfItFadedAfterItIsDrawn_ShowsFaded()
    {
        Frame Faded(DrawingMode mode)
        {
            var host = new Canvas { Width = Dim, Height = Dim, DrawingMode = mode };
            var part = new Square(Colors.Red);
            Canvas.SetLeft(part, 40);
            Canvas.SetTop(part, 40);
            host.Children.Add(part);
            using var stand = new Stand(host);
            stand.Render();
            part.Opacity = 0.3;
            return stand.Render();
        }

        var raster = Faded(DrawingMode.Raster);
        var vector = Faded(DrawingMode.Vector);
        Assert.That(raster.At(60, 60), Is.EqualTo(vector.At(60, 60)), "the faded part");
    }

    // What inside it melts at its edges melts in its texture too.
    [Test]
    public void ASoftEdgedPartOfIt_ShowsWhatItsShapesShow()
    {
        Frame Soft(DrawingMode mode)
        {
            var host = new Canvas { Width = Dim, Height = Dim, DrawingMode = mode };
            var part = new Square(Colors.Red) { SoftEdge = 12 };
            Canvas.SetLeft(part, 40);
            Canvas.SetTop(part, 40);
            host.Children.Add(part);
            using var stand = new Stand(host);
            stand.Render();
            return stand.Render();
        }

        var raster = Soft(DrawingMode.Raster);
        var vector = Soft(DrawingMode.Vector);
        Assert.That(raster.At(42, 42), Is.EqualTo(vector.At(42, 42)), "at its melting edge");
        AssertTheSame(raster, vector, "against its shapes' picture");
    }

    // Drawn anew square by square, a square a frame: what it held is laid down until the last square is in, then what
    // it draws now - the whole of it.
    [Test]
    public void DrawnAnewSquareBySquare_ShowsWhatItHeldUntilTheLastSquare_ThenWhatItDrawsNow()
    {
        var picture = new Picture { DrawingMode = DrawingMode.Raster, RasterTileSize = 64, RasterFrameBudget = TimeSpan.Zero };
        using var stand = new Stand(picture);
        stand.Render();
        picture.Paint = Colors.Yellow;
        picture.InvalidateRender(false);
        var next = stand.Render();
        Frame last = next;
        for (var i = 0; i < 12; i++)
        {
            last = stand.Render();
        }

        var wanted = new Stand(new Picture { DrawingMode = DrawingMode.Vector, Paint = Colors.Yellow });
        var vector = wanted.Render();
        wanted.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(next.At(40, 40), Is.EqualTo((255, 0, 0)), "the frame after the change: what it held");
            Assert.That(last.TilesDrawn, Is.EqualTo(0), "every square in: laid down from what it holds");
            AssertTheSame(last, vector, "once every square is in, against its shapes' picture");
        });
    }

    // A group drawn apart inside it opens its targets inside the tile's, in the tile's space.
    [TestCase(1.0, 0.0, 0.0)]
    [TestCase(1.6, -40.0, -30.0)]
    public void ARaster_HoldingAGroupCutByAPath_ShowsWhatItsShapesShow(double scale, double moveX, double moveY)
    {
        var vector = Once(DrawingMode.Vector, scale, moveX, moveY, true);
        var raster = Once(DrawingMode.Raster, scale, moveX, moveY, true);

        AssertTheSame(raster, vector, "against its shapes' picture");
    }

    [Test]
    public void AFrameThatChangesNothing_DrawsNoTile_AndShowsTheSame()
    {
        var picture = new Picture { DrawingMode = DrawingMode.Raster };
        using var stand = new Stand(picture);
        var first = stand.Render();
        var again = stand.Render();

        Assert.Multiple(() =>
        {
            Assert.That(first.TilesDrawn, Is.GreaterThan(0));
            Assert.That(again.TilesDrawn, Is.EqualTo(0), "laid down from the tiles it holds");
            AssertTheSame(again, first, "against the first frame");
        });
    }

    // The camera moving and zooming within a step of scale moves and scales the tiles it holds. A camera moves its
    // content as one (a motion node), drawing nothing of it again.
    [Test]
    public void AMoveWithinAStepOfScale_DrawsNoTile_AndShowsWhatItsShapesShow()
    {
        var picture = new Picture { DrawingMode = DrawingMode.Raster };
        var host = new Canvas
        {
            Width = Dim, Height = Dim, IsRenderMotionNode = true,
            RenderTransform = new Transform { ScaleX = 0.6, ScaleY = 0.6 }
        };
        host.Children.Add(picture);
        using var stand = new Stand(host);
        stand.Render();
        host.RenderTransform = new Transform { ScaleX = 0.9, ScaleY = 0.9, TranslateX = 12, TranslateY = 7 };
        var moved = stand.Render();
        var vector = Once(DrawingMode.Vector, 0.9, 12, 7);

        Assert.Multiple(() =>
        {
            Assert.That(moved.TilesDrawn, Is.EqualTo(0), "the tiles it held, moved and scaled");
            AssertTheSame(moved, vector, "against its shapes' picture");
        });
    }

    [Test]
    public void WhatItDrawsChanging_DrawsItsTilesAnew()
    {
        var picture = new Picture { DrawingMode = DrawingMode.Raster };
        using var stand = new Stand(picture);
        stand.Render();
        picture.Paint = Colors.Yellow;
        picture.InvalidateRender(false);
        var changed = stand.Render();

        Assert.Multiple(() =>
        {
            Assert.That(changed.TilesDrawn, Is.GreaterThan(0), "drawn anew");
            Assert.That(changed.At(40, 40), Is.EqualTo((255, 255, 0)).Or.EqualTo((255, 255, 1)), "the new paint");
        });
    }

    private sealed class Square(Color color) : MeasurableUIComponent
    {
        protected override Size<double> MeasureOverride(Size<double> availableSize) => new(40, 40);

        protected override void OnRender(IDrawingContext context) =>
            context.ForControl(this).DrawRectangle(new SolidColorBrush(color), new Rect(0, 0, 40, 40));
    }

    private static Frame InARoundedBox(DrawingMode mode)
    {
        var box = new Border
        {
            Width = 100, Height = 100, CornerRadius = new CornerRadius(30), ClipToBounds = true,
            Child = new Picture { DrawingMode = mode }
        };
        var host = new Canvas { Width = Dim, Height = Dim };
        Canvas.SetLeft(box, 30);
        Canvas.SetTop(box, 30);
        host.Children.Add(box);
        using var stand = new Stand(host);
        return stand.Render();
    }

    // Under a rounded clip it is drawn from its shapes - a texture is laid down cut square - and cut as they are: by
    // the box it lies in, rounded corners and all, nothing of it past them.
    [Test]
    public void ARasterUnderARoundedClip_IsCutAsItsShapesAre()
    {
        var vector = InARoundedBox(DrawingMode.Vector);
        var raster = InARoundedBox(DrawingMode.Raster);

        Assert.Multiple(() =>
        {
            Assert.That(raster.At(140, 140), Is.EqualTo((255, 255, 255)), "past the box: nothing of it");
            Assert.That(raster.At(33, 33), Is.EqualTo((255, 255, 255)), "in the box's rounded corner: nothing of it");
            AssertTheSame(raster, vector, "against its shapes' picture");
        });
    }

    // Two squares overlapping, faded as one: a layer.
    private static Canvas FadedPair(DrawingMode mode, bool layer = false)
    {
        var pair = new Canvas { Width = Dim, Height = Dim, Opacity = 0.5, DrawingMode = mode, IsLayer = layer };
        var under = new Square(Colors.Red);
        var over = new Square(Colors.Blue);
        Canvas.SetLeft(under, 20);
        Canvas.SetTop(under, 20);
        Canvas.SetLeft(over, 40);
        Canvas.SetTop(over, 40);
        pair.Children.Add(under);
        pair.Children.Add(over);
        return pair;
    }

    // A raster is faded as one, as a layer is: its texture laid down faded - in the frame after too.
    [Test]
    public void AFadedRaster_IsFadedAsOne()
    {
        var vector = new Stand(FadedPair(DrawingMode.Vector, true));
        var wanted = vector.Render();
        vector.Dispose();
        using var stand = new Stand(FadedPair(DrawingMode.Raster));
        var first = stand.Render();
        var again = stand.Render();

        // Where the two overlap, faded as one shows the upper over white; each faded apart shows it over the lower.
        Assert.Multiple(() =>
        {
            Assert.That(first.At(50, 50), Is.EqualTo(wanted.At(50, 50)), "the first frame, where they overlap");
            Assert.That(again.At(50, 50), Is.EqualTo(wanted.At(50, 50)), "the frame after, where they overlap");
            AssertTheSame(first, wanted, "the first frame against its shapes' picture");
            AssertTheSame(again, wanted, "the frame after against its shapes' picture");
        });
    }

    private sealed class Painted(SolidColorBrush brush) : MeasurableUIComponent
    {
        protected override Size<double> MeasureOverride(Size<double> availableSize) => new(40, 40);

        protected override void OnRender(IDrawingContext context) =>
            context.ForControl(this).DrawRectangle(brush, new Rect(0, 0, 40, 40));
    }

    // A part of it repainted in place - its group not recorded again - is drawn in the new paint: the texture holding
    // the old one is not laid down.
    [Test]
    public void APartRepaintedInPlace_DrawsItsTileAnew()
    {
        var brush = new SolidColorBrush(Colors.Blue);
        var square = new Painted(brush);
        var host = new Canvas { Width = Dim, Height = Dim, DrawingMode = DrawingMode.Raster };
        Canvas.SetLeft(square, 20);
        Canvas.SetTop(square, 20);
        host.Children.Add(square);
        using var stand = new Stand(host);
        stand.Render();
        brush.Color = Colors.Red;
        var repainted = stand.Render();

        Assert.That(repainted.At(40, 40), Is.EqualTo((255, 0, 0)), "the new paint");
    }

    // What is drawn after it is drawn where it is, over the whole window: closing a tile goes back to what the walk drew
    // within, not to the tile's 512 pixels - which had a viewer's panels past them vanish.
    [Test]
    public void WhatIsDrawnAfterIt_IsDrawnOverTheWholeWindow()
    {
        const int size = 700;
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new DeviceResourceFactory(device));
        using var renderer = new OffscreenTestRenderer(device, factory, size, size) { ClearColor = Colors.White };
        var host = new Canvas { Width = size, Height = size };
        host.Children.Add(new Picture { DrawingMode = DrawingMode.Raster });
        var after = new Square(Colors.Green);
        Canvas.SetLeft(after, 640);
        Canvas.SetTop(after, 640);
        host.Children.Add(after);
        var root = new VisualRoot(host, size, size);
        ((IMeasurableComponent)root).Measure(new Size<double>(size, size));
        ((IMeasurableComponent)root).Arrange(new Rect(0, 0, size, size));
        Assert.That(renderer.RenderFrame(root), Is.True);

        using var image = renderer.RenderTarget.ResolveTexture.ToImage();
        var bytes = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, bytes, 0, bytes.Length);
        var at = (660 * size + 660) * 4;
        Assert.That((bytes[at + 2], bytes[at + 1], bytes[at]), Is.EqualTo((0, 128, 0)), "the square past 512 pixels");
    }
}
