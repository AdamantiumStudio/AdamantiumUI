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
    private const double Radius = 8;
    private const int Sides = 16;

    private sealed class DrawnView(Action<IDrawingSession> draw) : MeasurableUIComponent
    {
        protected override Size<double> MeasureOverride(Size<double> availableSize) => new(Dim, Dim);

        protected override void OnRender(IDrawingContext context) => draw(context.ForControl(this));
    }

    private static ColoredMesh Disc(bool outlined, double centerX = 0, double centerY = 0)
    {
        var reach = (Radius + 1) / Math.Cos(Math.PI / Sides);
        var vertices = new List<ColoredVertex>();
        for (var side = 0; side < Sides; side++)
        {
            var start = 2 * Math.PI * side / Sides;
            var end = 2 * Math.PI * (side + 1) / Sides;
            vertices.Add(new ColoredVertex(new Vector2<double>(centerX, centerY), Colors.White));
            vertices.Add(new ColoredVertex(
                new Vector2<double>(centerX + reach * Math.Cos(start), centerY + reach * Math.Sin(start)), Colors.White));
            vertices.Add(new ColoredVertex(
                new Vector2<double>(centerX + reach * Math.Cos(end), centerY + reach * Math.Sin(end)), Colors.White));
        }

        var circle = new EllipseGeometry(new Rect(centerX - Radius, centerY - Radius, 2 * Radius, 2 * Radius));
        return outlined ? new ColoredMesh(vertices, circle) : new ColoredMesh(vertices);
    }

    private static byte[] Render(bool outlined, float zoom) => Render(Disc(outlined),
        Matrix4x4.CreateScale<float>(zoom, zoom, 1) * Matrix4x4.CreateTranslation<float>(Dim / 2f, Dim / 2f, 0));

    private static byte[] Render(ColoredMesh mesh, Matrix4x4F place)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new DeviceResourceFactory(device));
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Transparent };
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

        var column = (int)Math.Floor(Dim / 2.0 + (Radius * zoom + 2));
        Assert.That(AlphaAt(pixels, column, Dim / 2), Is.EqualTo(255), "the polygon reaches past the circle");
    }

    [Test]
    public void TrianglesLyingOverEachOther_CoverTheEdgeOnce()
    {
        var single = Disc(true);
        var doubled = new List<ColoredVertex>(single.Vertices);
        doubled.AddRange(single.Vertices);
        var place = Matrix4x4.CreateScale<float>(8, 8, 1) * Matrix4x4.CreateTranslation<float>(Dim / 2f, Dim / 2f, 0);

        var once = Render(single, place);
        var twice = Render(new ColoredMesh(doubled, single.Outline), place);

        var largest = 0;
        for (var at = 3; at < once.Length; at += 4)
        {
            largest = Math.Max(largest, Math.Abs(once[at] - twice[at]));
        }

        Assert.That(largest, Is.LessThanOrEqualTo(2), "the edge's pixels laid down by the topmost triangle alone");
    }

    [Test]
    public void TwoFiguresSharingASide_LeaveNoSeamAlongIt()
    {
        var vertices = new List<ColoredVertex>();
        void Square(double left)
        {
            Vector2<double>[] corners =
            [
                new(left, 0), new(left + 40, 0), new(left + 40, 40), new(left, 40)
            ];
            int[] order = [0, 1, 2, 0, 2, 3];
            foreach (var index in order)
            {
                vertices.Add(new ColoredVertex(corners[index], Colors.White));
            }
        }

        Square(0);
        Square(40);
        var outline = new StreamGeometry { FillRule = FillRule.NonZero };
        var figures = outline.Open();
        figures.BeginFigure(new Vector2<double>(0, 0), true, true).LineTo(new Vector2<double>(40, 0))
            .LineTo(new Vector2<double>(40, 40)).LineTo(new Vector2<double>(0, 40)).CloseFigure();
        figures.BeginFigure(new Vector2<double>(40, 0), true, true).LineTo(new Vector2<double>(80, 0))
            .LineTo(new Vector2<double>(80, 40)).LineTo(new Vector2<double>(40, 40)).CloseFigure();
        outline.RecalculateBounds();
        var place = Matrix4x4.CreateTranslation<float>(40.3f, 60.3f, 0);

        var pixels = Render(new ColoredMesh(vertices, outline), place);

        Assert.Multiple(() =>
        {
            for (var row = 64; row < 96; row += 4)
            {
                Assert.That(AlphaAt(pixels, 80, row), Is.EqualTo(255), $"on the shared side at row {row}");
            }
        });
    }

    [Test]
    public void TheEdge_LiesOnTheCurve_TurnedToo()
    {
        const float zoom = 5;
        const double offsetX = 4;
        const double offsetY = -2;
        var place = Matrix4x4.CreateScale<float>(zoom, zoom, 1) * Matrix4x4.CreateRotationZ<float>(0.6f) *
                    Matrix4x4.CreateTranslation<float>(Dim / 2f, Dim / 2f, 0);
        var pixels = Render(Disc(true, offsetX, offsetY), place);
        var centerX = offsetX * place.M11 + offsetY * place.M21 + place.M41;
        var centerY = offsetX * place.M12 + offsetY * place.M22 + place.M42;
        var radius = Radius * zoom;

        Assert.Multiple(() =>
        {
            for (var step = 0; step < 16; step++)
            {
                var angle = 2 * Math.PI * (step + 0.21) / 16;
                Assert.That(AlphaAt(pixels, (int)(centerX + (radius - 2) * Math.Cos(angle)),
                    (int)(centerY + (radius - 2) * Math.Sin(angle))), Is.EqualTo(255), $"inside at step {step}");
                Assert.That(AlphaAt(pixels, (int)(centerX + (radius + 2) * Math.Cos(angle)),
                    (int)(centerY + (radius + 2) * Math.Sin(angle))), Is.Zero, $"outside at step {step}");
            }
        });
    }
}
