using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A radial gradient between two circles apart paints the cone of circles from one to the other and nothing beside it,
// as PDF and Canvas 2D say - and the cone's sides are smooth, covered as a shape's edge is, not a step per pixel.
[TestFixture]
[Category("Gpu")]
public class TwoCircleGradientRenderTests
{
    private const int Dim = 128;

    public enum Path
    {
        // A geometry: drawn from its curves.
        Geometry,

        // A rectangle: the gradient the batches cannot carry, drawn by its unit.
        Rectangle
    }

    private sealed class DrawnView(Action<IDrawingSession> draw) : MeasurableUIComponent
    {
        protected override Size<double> MeasureOverride(Size<double> availableSize) => new(Dim, Dim);

        protected override void OnRender(IDrawingContext context) => draw(context.ForControl(this));
    }

    // A small circle low down, a large one higher up and to the right: the cone between them opens upward.
    private static RadialGradientBrush Cone() => new()
    {
        GradientOrigin = new Vector2<double>(0.3, 0.85),
        GradientOriginRadius = 0.03 / 0.25,
        Center = new Vector2<double>(0.5, 0.3),
        RadiusX = 0.25,
        RadiusY = 0.25,
        SpreadMethod = GradientSpreadMethod.Pad,
        GradientStops = { new GradientStop(Colors.Blue, 0), new GradientStop(Colors.HotPink, 1) }
    };

    private static byte[] Render(Path path)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new DeviceResourceFactory(device));
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Transparent };
        var box = new Rect(0, 0, Dim, Dim);
        var root = new VisualRoot(new DrawnView(session =>
        {
            if (path == Path.Geometry)
            {
                session.DrawGeometry(Cone(), new RectangleGeometry(box), null);
            }
            else
            {
                session.DrawRectangle(Cone(), box);
            }
        }), Dim, Dim);
        ((IMeasurableComponent)root).Measure(new Size<double>(Dim, Dim));
        ((IMeasurableComponent)root).Arrange(box);
        Assert.That(renderer.RenderFrame(root), Is.True, "off-screen frame must render");
        using var image = renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, pixels, 0, pixels.Length);
        return pixels;
    }

    private static int AlphaAt(byte[] pixels, double x, double y) =>
        pixels[((int)(y * Dim) * Dim + (int)(x * Dim)) * 4 + 3];

    [TestCase(Path.Geometry)]
    [TestCase(Path.Rectangle)]
    public void BesideTheCone_NothingIsPainted(Path path)
    {
        var pixels = Render(path);

        Assert.Multiple(() =>
        {
            Assert.That(AlphaAt(pixels, 0.5, 0.3), Is.EqualTo(255), "inside, at the large circle's middle");
            Assert.That(AlphaAt(pixels, 0.05, 0.5), Is.Zero, "left of the cone");
            Assert.That(AlphaAt(pixels, 0.9, 0.9), Is.Zero, "right of the cone, low down");
        });
    }

    [TestCase(Path.Geometry)]
    [TestCase(Path.Rectangle)]
    public void TheConesSides_AreSmooth(Path path)
    {
        var pixels = Render(path);

        // Across a row through the cone: clear, then partly covered, then whole - and back again past the other side.
        var row = Dim / 2;
        var partial = 0;
        var steps = 0;
        var before = pixels[row * Dim * 4 + 3];
        for (var column = 0; column < Dim; column++)
        {
            var alpha = pixels[(row * Dim + column) * 4 + 3];
            if (alpha is > 16 and < 240)
            {
                partial++;
            }

            if (Math.Min(alpha, before) <= 8 && Math.Max(alpha, before) >= 247)
            {
                steps++;
            }

            before = alpha;
        }

        Assert.Multiple(() =>
        {
            Assert.That(partial, Is.GreaterThanOrEqualTo(2), "a pixel partly covered at each side");
            Assert.That(steps, Is.Zero, "no jump from clear to whole between two pixels");
        });
    }
}
