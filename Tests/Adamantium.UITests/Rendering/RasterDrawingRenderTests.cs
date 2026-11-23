using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
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
            session.DrawGeometry(new SolidColorBrush(Paint), ring, new Pen(new SolidColorBrush(Colors.Blue), 3),
                Matrix4x4F.Identity);
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

        public void Dispose() => _renderer.Dispose();
    }

    // Pixels apart by more than an edge's softening may move them, between two frames.
    private static int Apart(Frame first, Frame second)
    {
        var apart = 0;
        for (var y = 0; y < Dim; y++)
        {
            for (var x = 0; x < Dim; x++)
            {
                var (r, g, b) = first.At(x, y);
                var (wr, wg, wb) = second.At(x, y);
                if (Math.Max(Math.Abs(r - wr), Math.Max(Math.Abs(g - wg), Math.Abs(b - wb))) > 24)
                {
                    apart++;
                }
            }
        }

        return apart;
    }

    private static Frame Once(DrawingMode mode, double scale = 1, double moveX = 0, double moveY = 0)
    {
        var picture = new Picture { DrawingMode = mode };
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
            // Its tiles are drawn at the next power of two and laid down smaller, which softens an edge by a pixel.
            Assert.That(Apart(vector, raster), Is.LessThanOrEqualTo(Dim * 3), "pixels apart from its shapes' picture");
        });
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
            Assert.That(Apart(first, again), Is.EqualTo(0));
        });
    }

    // The camera moving and zooming within a step of scale moves and scales the tiles it holds.
    [Test]
    public void AMoveWithinAStepOfScale_DrawsNoTile_AndShowsWhatItsShapesShow()
    {
        var picture = new Picture { DrawingMode = DrawingMode.Raster };
        var host = new Canvas { Width = Dim, Height = Dim, RenderTransform = new Transform { ScaleX = 0.6, ScaleY = 0.6 } };
        host.Children.Add(picture);
        using var stand = new Stand(host);
        stand.Render();
        host.RenderTransform = new Transform { ScaleX = 0.9, ScaleY = 0.9, TranslateX = 12, TranslateY = 7 };
        var moved = stand.Render();
        var vector = Once(DrawingMode.Vector, 0.9, 12, 7);

        Assert.Multiple(() =>
        {
            Assert.That(moved.TilesDrawn, Is.EqualTo(0), "the tiles it held, moved and scaled");
            Assert.That(Apart(vector, moved), Is.LessThanOrEqualTo(Dim * 3), "pixels apart from its shapes' picture");
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
}
