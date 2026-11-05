using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.Vulkan.Core;
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

    private sealed class PlacedText : MeasurableUIComponent
    {
        public readonly Placement Placement = new();
        public int Renders;
        private readonly TextLayout _layout;

        public PlacedText()
        {
            var font = DefaultFontFamily;
            _layout = new TextLayout(font.Typeface, font.Fonts[0]);
        }

        protected override Size<double> MeasureOverride(Size<double> availableSize)
        {
            _layout.ProcessText("HHH", 24, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return new Size<double>(Width, Height);
        }

        protected override void OnRender(IDrawingContext context)
        {
            Renders++;
            var size = new Size<double>(60, 30);
            var parameters = new TextRenderingParameters
            {
                HorizontalTextAlignment = HorizontalTextAlignment.Left,
                VerticalTextAlignment = VerticalTextAlignment.Top,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                Color = Colors.White,
                TextArea = new Rectangle<int>(new Vector2F().As<int>(), size.As<int>())
            };
            context.ForControl(this).DrawText(parameters, size, _layout, Brushes.White, Brushes.Transparent,
                Brushes.Transparent, Placement);
        }
    }

    // Textures from the device, glyphs from a font renderer of its own.
    private sealed class FontsAndTextures(IGraphicsDevice device) : IResourceFactory
    {
        private readonly DeviceResourceFactory _textures = new(device);
        private FontRenderer _fonts;

        public uint MaxTextureDimension => _textures.MaxTextureDimension;
        public uint MaxTextureArrayLayers => _textures.MaxTextureArrayLayers;
        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => _textures.CreateTexture(description, pixelData);
        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) => _textures.CreateTextureArray(description, layers);
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => _textures.ImportSharedSurface(descriptor);
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) =>
            _textures.CreateRenderTarget(width, height, msaa, format, desiredLayout);
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => _fonts ??= new FontRenderer(graphicsDevice);
    }

    private readonly record struct Lit(int Count, int Left, int Top, int Right, int Bottom);

    private OffscreenTestRenderer _renderer;
    private VisualRoot _root;
    private Canvas _holder;
    private PlacedShapes _shapes;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new FontsAndTextures(device)), Width, Height)
            { ClearColor = Colors.Black };
        _shapes = new PlacedShapes { Width = Width, Height = Height };
        _holder = new Canvas { Width = Width, Height = Height };
        _holder.Children.Add(_shapes);
        var canvas = new Canvas();
        canvas.Children.Add(_holder);
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
    public void APenInPixels_StaysAsWideUnderAZoom()
    {
        var pen = new Pen(Brushes.White, 2) { ThicknessUnit = PenThicknessUnit.Pixels };
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

    [TestCase(true)]
    [TestCase(false)]
    public void PlacedText_MovesWithoutBeingDrawnAgain(bool batched)
    {
        var wasBatched = FontRenderer.UseTextBatch;
        var wasSynchronous = FontAtlasStore.SynchronousFill;
        FontRenderer.UseTextBatch = batched;
        FontAtlasStore.SynchronousFill = true;
        try
        {
            _holder.Children.Remove(_shapes);
            var text = new PlacedText { Width = Width, Height = Height };
            text.Placement.Matrix = At(10, 10);
            _holder.Children.Add(text);
            Draw();
            var before = Draw();
            var renders = text.Renders;

            text.Placement.Matrix = At(70, 40);
            var after = Draw();

            Assert.Multiple(() =>
            {
                Assert.That(before.Count, Is.GreaterThan(20), "the text is drawn");
                Assert.That(after.Left - before.Left, Is.EqualTo(60).Within(1), "the run follows its placement");
                Assert.That(after.Top - before.Top, Is.EqualTo(30).Within(1));
                Assert.That(text.Renders, Is.EqualTo(renders), "a move is not a record");
            });
        }
        finally
        {
            FontRenderer.UseTextBatch = wasBatched;
            FontAtlasStore.SynchronousFill = wasSynchronous;
        }
    }

    [Test]
    public void APlacedDrawing_RidesAMotionNodeThatMoves_AndStillMovesByItsPlacement()
    {
        var turn = new Transform();
        _holder.IsRenderMotionNode = true;
        _holder.RenderTransform = turn;
        _shapes.Pen = new Pen(Brushes.White, 2);
        var placement = new Placement { Matrix = At(10, 10) };
        _shapes.Placements.Add(placement);
        var before = Draw();
        var renders = _shapes.Renders;

        turn.TranslateX = 30;
        var scrolled = Draw();
        placement.Matrix = At(10, 40);
        var placed = Draw();

        Assert.Multiple(() =>
        {
            Assert.That(scrolled.Left - before.Left, Is.EqualTo(30).Within(1), "the node carries the drawing");
            Assert.That(scrolled.Count, Is.EqualTo(before.Count).Within(4), "fill and outline together");
            Assert.That(placed.Left, Is.EqualTo(scrolled.Left).Within(1));
            Assert.That(placed.Top - scrolled.Top, Is.EqualTo(30).Within(1), "and the placement still moves it");
            Assert.That(_shapes.Renders, Is.EqualTo(renders), "neither move is a record");
        });
    }

    [Test]
    public void APlacedDrawing_RidesTheCompositor()
    {
        var turn = new Transform();
        _holder.RenderTransform = turn;
        var placement = new Placement { Matrix = At(10, 10) };
        _shapes.Placements.Add(placement);
        var before = Draw();

        turn.BeginAnimation(Transform.TranslateXProperty,
            new DoubleAnimation { From = 40, To = 41, Duration = TimeSpan.FromSeconds(10) });
        Compositor.StartPending();
        Assume.That(Compositor.EntryFor(turn), Is.Not.Null, "the slide must be composited");
        var slid = Draw();
        turn.CancelAnimation(Transform.TranslateXProperty);

        Assert.That(slid.Left - before.Left, Is.EqualTo(40).Within(2), "the placed fill slides with its element");
    }

    [Test]
    public void APlacedDrawingInALayer_MovesInIt()
    {
        _shapes.OpacityMask = Brushes.White;
        var placement = new Placement { Matrix = At(10, 10) };
        _shapes.Placements.Add(placement);
        var before = Draw();

        placement.Matrix = At(90, 50);
        var after = Draw();

        Assert.Multiple(() =>
        {
            Assert.That(before.Count, Is.GreaterThan(0), "the layer shows the drawing");
            Assert.That(after.Left - before.Left, Is.EqualTo(80).Within(1), "and shows it moved");
            Assert.That(after.Count, Is.EqualTo(before.Count).Within(4), "whole, not cut by the layer's region");
        });
    }

    [Test]
    public void ARecolorAndAMove_InOneFrame_StillReplay()
    {
        var fill = new SolidColorBrush(Colors.White);
        _shapes.Fill = fill;
        var placement = new Placement { Matrix = At(10, 10) };
        _shapes.Placements.Add(placement);
        var before = Draw();

        fill.Color = Colors.Yellow;
        placement.Matrix = At(40, 10);
        var after = Draw();

        Assert.Multiple(() =>
        {
            Assert.That(after.Left - before.Left, Is.EqualTo(30).Within(1));
            Assert.That(_renderer.Cache.LastFrameReplayed, Is.True, "a recolor does not turn a move into a walk");
        });
    }

    [Test]
    public void APlacementDrawnByASecondElement_IsRefused()
    {
        var placement = new Placement();
        _shapes.Placements.Add(placement);
        var other = new PlacedShapes { Width = Width, Height = Height };
        _holder.Children.Add(other);
        Draw();

        Assert.That(() => placement.DrawnBy(other), Throws.InvalidOperationException);
        _holder.Children.Remove(_shapes);
        Assert.That(() => placement.DrawnBy(other), Throws.Nothing, "once its element has left, another may take it");
    }

    [Test]
    public void ADashedPenInPixels_KeepsItsWidth_WhenOnlyThePlacementMoves()
    {
        _shapes.Fill = null;
        _shapes.Pen = new Pen(Brushes.White, 2, dashStrokeArray: [6, 2], startLineCap: PenLineCap.ConvexRound)
            { ThicknessUnit = PenThicknessUnit.Pixels };
        var placement = new Placement { Matrix = At(8, 8) };
        _shapes.Placements.Add(placement);
        var near = Draw().Count;
        var renders = _shapes.Renders;

        placement.Matrix = Matrix4x4.CreateScale<float>(4, 4, 1) * At(8, 8);
        Draw();
        var far = Draw().Count;

        Assert.Multiple(() =>
        {
            Assert.That(_shapes.Renders, Is.EqualTo(renders), "a move is not a record");
            Assert.That((double)far / near, Is.InRange(2.5, 5.5), "4x the outline at the same width");
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
