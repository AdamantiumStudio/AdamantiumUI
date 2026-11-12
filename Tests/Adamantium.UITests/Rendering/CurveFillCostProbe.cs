using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.UI.Rendering.Retained;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// Prints (does not assert) what a fill costs drawn from its curves against drawn from its triangulated mesh: the CPU
// preparation of each geometry, and a clean frame - replayed, the GPU idle at its end - at a large target. Writes the
// table to ADAM_PROBE_OUT, or to curve-fill-cost.txt in the temp folder.
[TestFixture]
[Category("Gpu")]
[Explicit("Measurement probe - run it deliberately and read the numbers")]
public class CurveFillCostProbe
{
    private const int Dim = 2048;
    private const int Frames = 30;

    private sealed record Scene(string Name, Func<List<Geometry>> Build, double Zoom = 1.0);

    private static readonly Scene[] Scenes =
    [
        new("glyphs: 4000 small cubic shapes", Glyphs),
        new("coastline: one polygon of 20000 lines", Coastline),
        new("hatch: one path of 2000 thin bars", Hatch),
        new("circles: 200 large, overlapping", Circles),
        new("circles at 800%", Circles, 8.0)
    ];

    [Test]
    public void FillCost_CurvesAgainstMesh()
    {
        var report = new StringBuilder();
        report.AppendLine($"target {Dim}x{Dim}, {Frames} clean frames each; ms");
        report.AppendLine("scene | prep mesh | prep curves | frame mesh | frame tiles | moving mesh | moving tiles | pieces | packed KB");
        var curvesWere = InstancedFillCollector.CurvesEnabled;
        var tilesWere = InstancedFillCollector.CurveTilesEnabled;
        try
        {
            foreach (var scene in Scenes)
            {
                var prep = Prep(scene);
                InstancedFillCollector.CurvesEnabled = false;
                var meshFrame = Frame(scene);
                var meshMoving = Frame(scene, moving: true);
                InstancedFillCollector.CurvesEnabled = true;
                InstancedFillCollector.CurveTilesEnabled = true;
                var tileFrame = Frame(scene);
                var tileMoving = Frame(scene, moving: true);
                report.AppendLine($"{scene.Name} | {prep.MeshMilliseconds:F1} | {prep.CurvesMilliseconds:F1} | " +
                                  $"{meshFrame:F2} | {tileFrame:F2} | {meshMoving:F2} | {tileMoving:F2} | " +
                                  $"{prep.Pieces} | {prep.Words * 4 / 1024}");
            }
        }
        finally
        {
            InstancedFillCollector.CurvesEnabled = curvesWere;
            InstancedFillCollector.CurveTilesEnabled = tilesWere;
        }

        var path = Environment.GetEnvironmentVariable("ADAM_PROBE_OUT")
                   ?? Path.Combine(Path.GetTempPath(), "curve-fill-cost.txt");
        File.WriteAllText(path, report.ToString());
        TestContext.Out.WriteLine(report.ToString());
        TestContext.Out.WriteLine($"written to {path}");
    }

    // The CPU side, each on geometries of its own so neither finds the other's work cached: triangulating for the mesh,
    // against cutting the curves and packing them for the shader.
    private static CurvePrepCost Prep(Scene scene)
    {
        var forMesh = scene.Build();
        var watch = Stopwatch.StartNew();
        foreach (var g in forMesh) g.ProcessGeometry(GeometryType.Both);
        var mesh = watch.Elapsed.TotalMilliseconds;

        var forCurves = scene.Build();
        var pieces = 0;
        var words = 0L;
        watch.Restart();
        foreach (var g in forCurves)
        {
            var fill = CurveFill.From(g.CurvesForDrawing);
            if (fill == null) continue;
            pieces += (int)fill.Words[4];
            words += fill.Words.Length;
        }

        return new CurvePrepCost(mesh, watch.Elapsed.TotalMilliseconds, pieces, words);
    }

    // A frame of the scene, clean - replayed - or, moving, with its transform turned, scaled and moved a little each
    // frame, as a scroll, a zoom or an animation does.
    private static double Frame(Scene scene, bool moving = false)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Transparent };

        var geometries = scene.Build();
        var brushes = Enumerable.Range(0, 8)
            .Select(i => (Brush)new SolidColorBrush(Color.FromRgba((byte)(40 + i * 25), 90, (byte)(220 - i * 20), 255)))
            .ToArray();
        var content = new TestControl
        {
            RenderAction = s =>
            {
                for (var i = 0; i < geometries.Count; i++) s.DrawGeometry(brushes[i % brushes.Length], geometries[i]);
            }
        };
        content.Bounds = new Rect(0, 0, Dim, Dim);
        content.RenderSize = new Size<double>(Dim, Dim);
        if (scene.Zoom != 1.0)
        {
            content.RenderTransform = new Transform
            {
                ScaleX = scene.Zoom, ScaleY = scene.Zoom,
                TranslateX = -Dim * (scene.Zoom - 1) / 2, TranslateY = -Dim * (scene.Zoom - 1) / 2
            };
        }

        var root = new VisualRoot(content, Dim, Dim);
        Assert.That(renderer.RenderFrame(root), Is.True);

        for (var i = 0; i < 5; i++) renderer.RenderAgain(root);
        var watch = new Stopwatch();
        var times = new double[Frames];
        for (var i = 0; i < Frames; i++)
        {
            if (moving)
            {
                var zoom = scene.Zoom * (1 + 0.01 * (i % 3));
                content.RenderTransform = new Transform
                {
                    ScaleX = zoom, ScaleY = zoom, RotationAngle = i % 4,
                    RotationCenterX = Dim / 2.0, RotationCenterY = Dim / 2.0,
                    TranslateX = -Dim * (zoom - 1) / 2 + i % 5, TranslateY = -Dim * (zoom - 1) / 2 + i % 7
                };
            }

            watch.Restart();
            if (moving) renderer.RenderFrame(root);
            else renderer.RenderAgain(root);
            times[i] = watch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);
        return times[Frames / 2];
    }

    private static List<Geometry> Glyphs()
    {
        var random = new Random(7);
        var shapes = new List<Geometry>(4000);
        for (var n = 0; n < 4000; n++)
        {
            var cx = random.NextDouble() * (Dim - 60) + 30;
            var cy = random.NextDouble() * (Dim - 60) + 30;
            var size = 10 + random.NextDouble() * 30;
            var lobes = 6 + random.Next(5);
            var g = new StreamGeometry();
            var figure = g.Open().BeginFigure(Lobe(cx, cy, size, 0, lobes, random), true, true);
            for (var k = 0; k < lobes; k++)
            {
                var a0 = Math.Tau * k / lobes;
                var a1 = Math.Tau * (k + 1) / lobes;
                var r = size * (0.6 + random.NextDouble() * 0.4);
                figure.CubicBezierTo(
                    new Vector2<double>(cx + Math.Cos(a0 + 0.3) * r * 1.4, cy + Math.Sin(a0 + 0.3) * r * 1.4),
                    new Vector2<double>(cx + Math.Cos(a1 - 0.3) * r * 0.5, cy + Math.Sin(a1 - 0.3) * r * 0.5),
                    Lobe(cx, cy, size, k + 1, lobes, random));
            }

            shapes.Add(g);
        }

        return shapes;
    }

    private static Vector2<double> Lobe(double cx, double cy, double size, int k, int lobes, Random random)
    {
        var a = Math.Tau * (k % lobes) / lobes;
        return new Vector2<double>(cx + Math.Cos(a) * size, cy + Math.Sin(a) * size);
    }

    private static List<Geometry> Coastline()
    {
        var random = new Random(11);
        var points = new Vector2<double>[20000];
        var r = 900.0;
        for (var i = 0; i < points.Length; i++)
        {
            r = Math.Clamp(r + (random.NextDouble() - 0.5) * 24, 600, 1000);
            var a = Math.Tau * i / points.Length;
            points[i] = new Vector2<double>(Dim / 2.0 + Math.Cos(a) * r, Dim / 2.0 + Math.Sin(a) * r);
        }

        var g = new StreamGeometry();
        g.Open().BeginFigure(points[0], true, true).PolylineLineTo(points[1..], true);
        return [g];
    }

    private static List<Geometry> Hatch()
    {
        var g = new StreamGeometry();
        var context = g.Open();
        for (var i = 0; i < 2000; i++)
        {
            var x = 20 + i * ((Dim - 40) / 2000.0);
            context.BeginFigure(new Vector2<double>(x, 20), true, true)
                .PolylineLineTo([new(x + 0.5, 20), new(x + 0.5, Dim - 20), new(x, Dim - 20)], true);
        }

        return [g];
    }

    private static List<Geometry> Circles()
    {
        var random = new Random(3);
        var shapes = new List<Geometry>(200);
        for (var i = 0; i < 200; i++)
        {
            var r = 150 + random.NextDouble() * 250;
            shapes.Add(new EllipseGeometry(new Vector2<double>(random.NextDouble() * Dim, random.NextDouble() * Dim), r, r));
        }

        return shapes;
    }

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
