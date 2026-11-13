using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A colored mesh cut to its outline: the triangles give the color, the outline the edge - drawn by the curve coverage
// every fill's edge comes from, so it lies on the curve and is smooth at any zoom, however coarse the triangles.
[TestFixture]
[Category("Gpu")]
public class ColoredMeshOutlineRenderTests
{
    private const int Dim = 160;

    // A circle of this radius about the middle, in the mesh's units.
    private const double Radius = 8;
    private const int Sides = 16;

    private sealed class DrawnView(Action<IDrawingSession> draw) : MeasurableUIComponent
    {
        protected override Size<double> MeasureOverride(Size<double> availableSize) => new(Dim, Dim);

        protected override void OnRender(IDrawingContext context) => draw(context.ForControl(this));
    }

    // Sixteen triangles round the middle, their outer edge a polygon a little outside the circle - coarse on purpose.
    private static ColoredMesh Disc(bool outlined)
    {
        var reach = (Radius + 1) / Math.Cos(Math.PI / Sides);
        var vertices = new List<ColoredVertex>();
        for (var side = 0; side < Sides; side++)
        {
            var from = 2 * Math.PI * side / Sides;
            var to = 2 * Math.PI * (side + 1) / Sides;
            vertices.Add(new ColoredVertex(new Vector2<double>(0, 0), Colors.White));
            vertices.Add(new ColoredVertex(new Vector2<double>(reach * Math.Cos(from), reach * Math.Sin(from)), Colors.White));
            vertices.Add(new ColoredVertex(new Vector2<double>(reach * Math.Cos(to), reach * Math.Sin(to)), Colors.White));
        }

        var circle = new EllipseGeometry(new Rect(-Radius, -Radius, 2 * Radius, 2 * Radius));
        return outlined ? new ColoredMesh(vertices, circle) : new ColoredMesh(vertices);
    }

    // The disc drawn `zoom` times its size about the window's middle.
    private static byte[] Render(bool outlined, float zoom)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new DeviceResourceFactory(device));
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Transparent };
        var place = Matrix4x4.CreateScale<float>(zoom, zoom, 1) * Matrix4x4.CreateTranslation<float>(Dim / 2f, Dim / 2f, 0);
        var mesh = Disc(outlined);
        var box = new Rect(0, 0, Dim, Dim);
        var root = new VisualRoot(new DrawnView(session => session.DrawColoredMesh(mesh, place)), Dim, Dim);
        ((IMeasurableComponent)root).Measure(new Size<double>(Dim, Dim));
        ((IMeasurableComponent)root).Arrange(box);
        Assert.That(renderer.RenderFrame(root), Is.True, "off-screen frame must render");
        using var image = renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, pixels, 0, pixels.Length);
        return pixels;
    }

    private static int AlphaAt(byte[] pixels, int column, int row) => pixels[(row * Dim + column) * 4 + 3];

    [Test]
    public void TheEdge_LiesOnTheCurve_AtADeepZoom()
    {
        const float zoom = 8;
        var pixels = Render(true, zoom);
        var radius = Radius * zoom;

        Assert.Multiple(() =>
        {
            for (var step = 0; step < 24; step++)
            {
                var angle = 2 * Math.PI * (step + 0.37) / 24;
                var inside = (int)Math.Floor(Dim / 2.0 + (radius - 2) * Math.Cos(angle));
                var insideRow = (int)Math.Floor(Dim / 2.0 + (radius - 2) * Math.Sin(angle));
                var outside = (int)Math.Floor(Dim / 2.0 + (radius + 2) * Math.Cos(angle));
                var outsideRow = (int)Math.Floor(Dim / 2.0 + (radius + 2) * Math.Sin(angle));
                Assert.That(AlphaAt(pixels, inside, insideRow), Is.EqualTo(255), $"two pixels inside at step {step}");
                Assert.That(AlphaAt(pixels, outside, outsideRow), Is.Zero, $"two pixels outside at step {step}");
            }
        });
    }

    [Test]
    public void TheEdge_IsSmooth()
    {
        var pixels = Render(true, 8);

        var partial = 0;
        var steps = 0;
        for (var row = 0; row < Dim; row++)
        {
            for (var column = 1; column < Dim; column++)
            {
                var alpha = AlphaAt(pixels, column, row);
                var before = AlphaAt(pixels, column - 1, row);
                partial += alpha is > 8 and < 247 ? 1 : 0;
                steps += Math.Min(alpha, before) <= 8 && Math.Max(alpha, before) >= 247 ? 1 : 0;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(partial, Is.GreaterThan(100), "the circle's edge partly covers the pixels it crosses");
            Assert.That(steps * 20, Is.LessThan(partial), "clear beside whole only where the edge runs along a side");
        });
    }

    [Test]
    public void WithoutAnOutline_TheTrianglesKeepTheirOwnEdge()
    {
        const float zoom = 8;
        var pixels = Render(false, zoom);

        // The polygon reaches past the circle: two pixels outside it is still painted.
        var column = (int)Math.Floor(Dim / 2.0 + (Radius * zoom + 2));
        Assert.That(AlphaAt(pixels, column, Dim / 2), Is.EqualTo(255));
    }
}
