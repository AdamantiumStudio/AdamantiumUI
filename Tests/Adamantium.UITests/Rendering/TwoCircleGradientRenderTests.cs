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

    /// <summary>How the gradient reaches the screen.</summary>
    public enum Path
    {
        /// <summary>A geometry, drawn from its curves.</summary>
        Geometry,

        /// <summary>A rectangle, drawn by its unit.</summary>
        Rectangle
    }

    private sealed class DrawnView(Action<IDrawingSession> draw) : MeasurableUIComponent
    {
        protected override Size<double> MeasureOverride(Size<double> availableSize) => new(Dim, Dim);

        protected override void OnRender(IDrawingContext context) => draw(context.ForControl(this));
    }

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

    private static RadialGradientBrush Nested(double focusX) => new()
    {
        GradientOrigin = new Vector2<double>((focusX + 0.5) / Dim, (Dim / 2 + 0.5) / Dim),
        GradientOriginRadius = 1e-9,
        Center = new Vector2<double>((Dim / 2 + 0.5) / Dim, (Dim / 2 + 0.5) / Dim),
        RadiusX = 0.4,
        RadiusY = 0.4,
        SpreadMethod = GradientSpreadMethod.Pad,
        GradientStops = { new GradientStop(Colors.Blue, 0), new GradientStop(Colors.HotPink, 1) }
    };

    private static byte[] Render(Path path) => Render(path, Cone);

    private static byte[] Render(Path path, Func<Brush> brush)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new DeviceResourceFactory(device));
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Transparent };
        var box = new Rect(0, 0, Dim, Dim);
        var root = new VisualRoot(new DrawnView(session =>
        {
            if (path == Path.Geometry)
            {
                session.DrawGeometry(brush(), new RectangleGeometry(box), null);
            }
            else
            {
                session.DrawRectangle(brush(), box);
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

    [TestCase(Path.Geometry, Dim / 2)]
    [TestCase(Path.Rectangle, Dim / 2)]
    [TestCase(Path.Geometry, Dim / 2 - 20)]
    [TestCase(Path.Rectangle, Dim / 2 - 20)]
    public void OneCircleInsideTheOther_IsWhole_AtItsFocusToo(Path path, int focusX)
    {
        var pixels = Render(path, () => Nested(focusX));

        var holes = 0;
        for (var at = 3; at < pixels.Length; at += 4)
        {
            holes += pixels[at] == 255 ? 0 : 1;
        }

        Assert.Multiple(() =>
        {
            Assert.That(pixels[((Dim / 2) * Dim + focusX) * 4 + 3], Is.EqualTo(255), "at the focus");
            Assert.That(holes, Is.Zero, "every pixel painted whole: padded past the end circle, one circle on each");
        });
    }

    [TestCase(Path.Geometry)]
    [TestCase(Path.Rectangle)]
    public void TheConesSides_AreSmooth(Path path)
    {
        var pixels = Render(path);

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
