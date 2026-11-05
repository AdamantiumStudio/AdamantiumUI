using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A placed draw moves by its placement's matrix: a slot write and a replay - the element is not drawn again.
[TestFixture]
[Category("Gpu")]
public class PlacementRenderTests
{
    private const int Width = 160;
    private const int Height = 96;

    private sealed class PlacedShapes : MeasurableUIComponent
    {
        public readonly List<Placement> Placements = [];
        public int Renders;
        public Pen Pen;
        public Brush Fill = Brushes.White;
        public bool Placed = true;

        protected override void OnRender(IDrawingContext context)
        {
            base.OnRender(context);
            Renders++;
            var session = context.ForControl(this);
            foreach (var placement in Placements)
            {
                var square = new RectangleGeometry(new Rect(0, 0, 16, 16));
                if (Placed)
                {
                    session.DrawGeometry(Fill, square, Pen, placement);
                }
                else
                {
                    session.DrawGeometry(Fill, square, Pen);
                }
            }
        }
    }

    private readonly record struct Lit(int Count, int Left, int Top, int Right, int Bottom);

    private OffscreenTestRenderer _renderer;
    private VisualRoot _root;
    private PlacedShapes _shapes;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)), Width, Height)
            { ClearColor = Colors.Black };
        _shapes = new PlacedShapes { Width = Width, Height = Height };
        var canvas = new Canvas();
        canvas.Children.Add(_shapes);
        _root = new VisualRoot(canvas, Width, Height);
    }

    [TearDown]
    public void TearDown() => _renderer.Dispose();

    private static Matrix4x4F At(float x, float y) => Matrix4x4.CreateTranslation<float>(x, y, 0);

    private Lit Draw()
    {
        ((IMeasurableComponent)_root).Measure(new Size<double>(Width, Height));
        ((IMeasurableComponent)_root).Arrange(new Rect(0, 0, Width, Height));
        Assert.That(_renderer.RenderFrame(_root), Is.True, "off-screen frame must render");
        RenderDirty.Clear();
        return Shown();
    }

    private Lit Shown()
    {
        using var img = _renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);
        int count = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 128 && pixels[i + 1] < 128 && pixels[i + 2] < 128)
            {
                continue;
            }

            var x = i / 4 % Width;
            var y = i / 4 / Width;
            count++;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        return new Lit(count, left, top, right, bottom);
    }

    [Test]
    public void MovingAPlacement_MovesTheDrawing_WithoutDrawingTheElementAgain()
    {
        var placement = new Placement { Matrix = At(10, 10) };
        _shapes.Placements.Add(placement);
        var before = Draw();
        var renders = _shapes.Renders;

        placement.Matrix = At(70, 40);
        var after = Draw();

        Assert.Multiple(() =>
        {
            Assert.That(before.Left, Is.EqualTo(10).Within(1));
            Assert.That(after.Left - before.Left, Is.EqualTo(60).Within(1), "the drawing follows the placement");
            Assert.That(after.Top - before.Top, Is.EqualTo(30).Within(1));
            Assert.That(after.Count, Is.EqualTo(before.Count).Within(4), "the same square, only moved");
            Assert.That(_shapes.Renders, Is.EqualTo(renders), "a move is not a record");
            Assert.That(_renderer.Cache.LastFrameReplayed, Is.True, "a move is a replay, not a walk");
        });
    }

    [Test]
    public void APlacementScaled_ScalesTheDrawing()
    {
        var placement = new Placement { Matrix = At(10, 10) };
        _shapes.Placements.Add(placement);
        var before = Draw();

        placement.Matrix = Matrix4x4.CreateScale<float>(2, 2, 1) * At(10, 10);
        var after = Draw();

        Assert.That(after.Right - after.Left + 1, Is.EqualTo(2 * (before.Right - before.Left + 1)).Within(2));
    }

    [Test]
    public void EachPlacement_MovesOnlyItsOwnDrawing()
    {
        var first = new Placement { Matrix = At(10, 10) };
        var second = new Placement { Matrix = At(100, 10) };
        _shapes.Placements.Add(first);
        _shapes.Placements.Add(second);
        var before = Draw();

        second.Matrix = At(100, 60);
        var after = Draw();

        Assert.Multiple(() =>
        {
            Assert.That(after.Top, Is.EqualTo(before.Top).Within(1), "the first square stays");
            Assert.That(after.Bottom - before.Bottom, Is.EqualTo(50).Within(1), "the second one moved down");
        });
    }

    [Test]
    public void APlacedStroke_MovesWithItsFill()
    {
        _shapes.Fill = null;
        _shapes.Pen = new Pen(Brushes.White, 2);
        var placement = new Placement { Matrix = At(10, 10) };
        _shapes.Placements.Add(placement);
        var before = Draw();
        var renders = _shapes.Renders;

        placement.Matrix = At(70, 40);
        var after = Draw();

        Assert.Multiple(() =>
        {
            Assert.That(before.Count, Is.GreaterThan(0), "the outline is drawn");
            Assert.That(after.Left - before.Left, Is.EqualTo(60).Within(1), "the outline follows the placement");
            Assert.That(after.Top - before.Top, Is.EqualTo(30).Within(1));
            Assert.That(_shapes.Renders, Is.EqualTo(renders), "a move is not a record");
        });
    }

    private int OutlineAt(float scale, Pen pen)
    {
        _shapes.Fill = null;
        _shapes.Pen = pen;
        _shapes.Placements.Clear();
        _shapes.Placements.Add(new Placement { Matrix = Matrix4x4.CreateScale<float>(scale, scale, 1) * At(8, 8) });
        _shapes.InvalidateRender(false);
        return Draw().Count;
    }

    [Test]
    public void APenInDevicePixels_StaysAsWideUnderAZoom()
    {
        var pen = new Pen(Brushes.White, 2) { ThicknessUnit = PenThicknessUnit.DevicePixels };
        var near = OutlineAt(1, pen);
        var far = OutlineAt(4, pen);

        // An outline 4x as long at the same width lights about 4x the pixels; a local width would light about 16x.
        Assert.That((double)far / near, Is.InRange(3.0, 5.0));
    }

    [Test]
    public void APenInLocalUnits_WidensUnderAZoom()
    {
        var pen = new Pen(Brushes.White, 2);
        var near = OutlineAt(1, pen);
        var far = OutlineAt(4, pen);

        Assert.That((double)far / near, Is.GreaterThan(10.0));
    }

    [Test]
    public void AMinimumDeviceThickness_KeepsAThinLineOnScreen()
    {
        // 0.02 units at 5x: a tenth of a pixel.
        var thin = OutlineAt(5, new Pen(Brushes.White, 0.02));
        var held = OutlineAt(5, new Pen(Brushes.White, 0.02) { MinimumDeviceThickness = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(thin, Is.LessThan(held / 4), "a tenth of a pixel lights almost nothing");
            Assert.That(held, Is.GreaterThan(4 * 16 * 5 / 2), "held at a pixel, the whole outline shows");
        });
    }

    [Test]
    public void APlacementMovedBeforeTheFirstFrame_IsDrawnWhereItIsNow()
    {
        var placement = new Placement { Matrix = At(10, 10) };
        _shapes.Placements.Add(placement);
        placement.Matrix = At(50, 20);

        var shown = Draw();

        Assert.That(shown.Left, Is.EqualTo(50).Within(1));
    }

    [Test]
    public void APlacementNoLongerDrawn_GivesItsSlotBack()
    {
        _shapes.Placements.Add(new Placement { Matrix = At(10, 10) });
        _shapes.Placements.Add(new Placement { Matrix = At(60, 10) });
        _shapes.Placed = false;
        Draw();
        var unplaced = _renderer.Cache.LiveTransformSlots;

        _shapes.Placed = true;
        _shapes.InvalidateRender(false);
        Draw();
        var placed = _renderer.Cache.LiveTransformSlots;

        _shapes.Placed = false;
        _shapes.InvalidateRender(false);
        Draw();

        Assert.Multiple(() =>
        {
            Assert.That(placed, Is.GreaterThanOrEqualTo(unplaced + 1), "each placement holds a slot of its own");
            Assert.That(_renderer.Cache.LiveTransformSlots, Is.EqualTo(unplaced), "and gives it back");
        });
    }
}
