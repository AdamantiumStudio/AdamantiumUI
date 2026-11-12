using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.UI.Rendering.Retained;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>
/// A solid curve fill covered from the flush's screen tiles must cover what the same fill covered from its own bands:
/// the tiles only change which pieces a pixel walks, and the winding they hand over for the rest is exact. Compared pixel
/// by pixel over fills that cross many tiles - holes, both fill rules, lines, curves, turned and scaled.
/// </summary>
[TestFixture]
[Category("Gpu")]
public class CurveTileRenderTests
{
    private const int Dim = 320;

    private static byte[] Render(Func<List<Geometry>> build, bool tiles, Transform transform = null)
    {
        var tilesWere = InstancedFillCollector.CurveTilesEnabled;
        InstancedFillCollector.CurveTilesEnabled = tiles;
        try
        {
            var device = GpuTestDevice.Device;
            var factory = new RenderUnitFactory(device, new StubResourceFactory());
            using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Transparent };
            var geometries = build();
            var content = new TestControl
            {
                RenderAction = s =>
                {
                    foreach (var g in geometries) s.DrawGeometry(Brushes.Red, g);
                }
            };
            content.Bounds = new Rect(0, 0, Dim, Dim);
            content.RenderSize = new Size<double>(Dim, Dim);
            if (transform != null) content.RenderTransform = transform;

            var root = new VisualRoot(content, Dim, Dim);
            Assert.That(renderer.RenderFrame(root), Is.True);
            // Twice: the lists are sized from what the frame before them placed.
            Assert.That(renderer.RenderAgain(root), Is.True);
            if (Environment.GetEnvironmentVariable("ADAM_TILE_DUMP") is { } dump)
            {
                renderer.Save(System.IO.Path.Combine(dump,
                    $"{TestContext.CurrentContext.Test.Name}-{(tiles ? "tiles" : "bands")}.png"), ImageFileType.Png);
            }

            using var img = renderer.RenderTarget.ResolveTexture.ToImage();
            var bytes = new byte[(int)img.TotalSizeInBytes];
            Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            InstancedFillCollector.CurveTilesEnabled = tilesWere;
        }
    }

    private static int Alpha(byte[] pixels, int x, int y) => pixels[(y * Dim + x) * 4 + 3];

    // Whether a pixel lies on an edge of the banded picture: it or a neighbour is partly covered, or its neighbours
    // disagree. The two ways soften an edge alike but not to the step - along a turned edge the bands' rays run the
    // fill's own axes and the tiles' the screen's - while a winding the tiles got wrong would flip a pixel inside.
    private static bool OnEdge(byte[] pixels, int x, int y)
    {
        var seen0 = false;
        var seen255 = false;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var a = Alpha(pixels, Math.Clamp(x + dx, 0, Dim - 1), Math.Clamp(y + dy, 0, Dim - 1));
            if (a is > 0 and < 255) return true;
            seen0 |= a == 0;
            seen255 |= a == 255;
        }

        return seen0 && seen255;
    }

    private static void AssertSameCoverage(Func<List<Geometry>> build, Transform transform = null)
    {
        var banded = Render(build, tiles: false, transform);
        var tiled = Render(build, tiles: true, transform);
        var worst = 0;
        var inside = 0;
        var covered = 0;
        for (var y = 0; y < Dim; y++)
        for (var x = 0; x < Dim; x++)
        {
            var d = Math.Abs(Alpha(banded, x, y) - Alpha(tiled, x, y));
            worst = Math.Max(worst, d);
            if (d > 2 && !OnEdge(banded, x, y)) inside++;
            if (Alpha(banded, x, y) > 0) covered++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(covered, Is.GreaterThan(100), "the scene draws something");
            Assert.That(inside, Is.Zero, "away from the edges every pixel is covered alike");
            Assert.That(worst, Is.LessThanOrEqualTo(100), "and no edge pixel flips");
        });
    }

    private static Geometry Star(double cx, double cy, double r, int points, FillRule rule)
    {
        var g = new StreamGeometry { FillRule = rule };
        var figure = g.Open().BeginFigure(new Vector2<double>(cx + r, cy), true, true);
        var corners = new List<Vector2<double>>();
        for (var i = 1; i < points; i++)
        {
            var a = Math.Tau * i * 2 / points;
            corners.Add(new Vector2<double>(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }

        figure.PolylineLineTo(corners, true);
        return g;
    }

    [Test]
    public void StarsUnderBothRules_CoverAsTheirBandsDo() =>
        AssertSameCoverage(() =>
        [
            Star(90, 90, 80, 7, FillRule.EvenOdd),
            Star(230, 100, 75, 5, FillRule.NonZero),
            Star(160, 240, 70, 9, FillRule.EvenOdd)
        ]);

    [Test]
    public void ARingWithAHole_AndCurvedLobes_CoverAsTheirBandsDo() =>
        AssertSameCoverage(() =>
        {
            var ring = new StreamGeometry { FillRule = FillRule.EvenOdd };
            var context = ring.Open();
            context.BeginFigure(new Vector2<double>(160, 20), true, true)
                .ArcTo(new Vector2<double>(160, 300), new Size<double>(140, 140), 0, false, SweepDirection.Clockwise)
                .ArcTo(new Vector2<double>(160, 20), new Size<double>(140, 140), 0, false, SweepDirection.Clockwise);
            context.BeginFigure(new Vector2<double>(160, 90), true, true)
                .CubicBezierTo(new Vector2<double>(260, 60), new Vector2<double>(250, 260), new Vector2<double>(160, 230))
                .CubicBezierTo(new Vector2<double>(40, 200), new Vector2<double>(90, 80), new Vector2<double>(160, 90));
            return [ring];
        });

    [Test]
    public void ADensePolygonAndThinBars_CoverAsTheirBandsDo() =>
        AssertSameCoverage(() =>
        {
            var random = new Random(5);
            var points = new List<Vector2<double>>();
            var r = 120.0;
            for (var i = 0; i < 3000; i++)
            {
                r = Math.Clamp(r + (random.NextDouble() - 0.5) * 6, 80, 150);
                var a = Math.Tau * i / 3000;
                points.Add(new Vector2<double>(160 + Math.Cos(a) * r, 160 + Math.Sin(a) * r));
            }

            var coast = new StreamGeometry();
            coast.Open().BeginFigure(points[0], true, true).PolylineLineTo(points.GetRange(1, points.Count - 1), true);

            var bars = new StreamGeometry();
            var context = bars.Open();
            for (var i = 0; i < 60; i++)
            {
                var x = 10 + i * 5.03;
                context.BeginFigure(new Vector2<double>(x, 12), true, true)
                    .PolylineLineTo([new(x + 1.7, 12), new(x + 1.7, 60.5), new(x, 60.5)], true);
            }

            return [coast, bars];
        });

    [Test]
    public void TurnedAndScaled_CoverAsTheirBandsDo() =>
        AssertSameCoverage(
            () => [Star(100, 100, 70, 7, FillRule.NonZero), new EllipseGeometry(new Vector2<double>(110, 90), 30, 50)],
            new Transform { RotationAngle = 23, ScaleX = 1.4, ScaleY = 1.4, TranslateX = 20, TranslateY = 10 });

    // The unit factory needs one, but nothing here draws a texture or text.
    private sealed class StubResourceFactory : IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => throw new NotSupportedException();
    }
}
