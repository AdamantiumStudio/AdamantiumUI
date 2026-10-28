using System;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Rendering;
using Adamantium.UI.Rendering.RenderUnits;
using Adamantium.Vulkan.Core;
using NUnit.Framework;
using Image = Adamantium.Imaging.Image;

namespace Adamantium.UITests.Rendering;

// A faded group drawn as a layer: its children are laid down at full strength into a texture of their own, and the
// texture once with the group's opacity - so where they overlap, the background shows through as much as anywhere else.
[TestFixture]
[Category("Gpu")]
public class LayerOpacityRenderTests
{
    private IGraphicsDevice _device;

    [SetUp]
    public void SetUp() => _device = GpuTestDevice.Device;

    [TearDown]
    public void TearDown() => AnimationManager.Reset();

    // Two 20x20 red squares overlapping by 10 px, in a group at half opacity, over blue.
    private static (TestRoot Root, TestControl Group) Overlap(bool layer)
    {
        var root = new TestRoot(64, 64);
        var group = new TestControl { Opacity = 0.5, IsLayer = layer };
        group.Bounds = new Rect(4, 4, 40, 30);
        group.RenderSize = new Size<double>(40, 30);
        foreach (var x in new[] { 0.0, 10.0 })
        {
            var square = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 20, 20)) };
            square.Bounds = new Rect(x, 0, 20, 20);
            square.RenderSize = new Size<double>(20, 20);
            group.Add(square);
        }

        root.Add(group);
        return (root, group);
    }

    // Group at (4,4): the first square alone covers x 4..13, both cover x 14..23.
    private (uint Single, uint Both) Render(TestRoot root) => Render(root, (8, 14), (18, 14));

    private (uint First, uint Second) Render(TestRoot root, (int X, int Y) first, (int X, int Y) second)
    {
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        Assert.That(renderer.RenderFrame(root), Is.True);
        return Pixels(renderer, first, second);
    }

    private static (uint Single, uint Both) Pixels(OffscreenTestRenderer renderer) => Pixels(renderer, (8, 14), (18, 14));

    private static (uint First, uint Second) Pixels(OffscreenTestRenderer renderer, (int X, int Y) first, (int X, int Y) second)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"layer_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var px = Image.Load(path).GetPixelBuffer(0, 0);
            return (px.GetPixel<uint>(first.X, first.Y), px.GetPixel<uint>(second.X, second.Y));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static int Red(uint pixel) => (int)(pixel & 0xFF);

    private static int Blue(uint pixel) => (int)((pixel >> 16) & 0xFF);

    [Test]
    public void ALayer_LetsTheBackgroundThroughEvenlyWhereChildrenOverlap()
    {
        var (single, both) = Render(Overlap(layer: true).Root);

        Assert.Multiple(() =>
        {
            Assert.That(Red(single), Is.InRange(110, 145), "half red over blue where one square is");
            Assert.That(Red(both), Is.EqualTo(Red(single)).Within(3), "and the same where both are");
            Assert.That(Blue(both), Is.EqualTo(Blue(single)).Within(3));
        });
    }

    [Test]
    public void AReplayedFrame_LaysTheLayerDownAgain()
    {
        var (root, _) = Overlap(layer: true);
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);

        Assert.That(renderer.Cache.LastDrawPath, Is.EqualTo(DrawPath.Replay), "nothing changed, so the stream is replayed");
        var (single, both) = Pixels(renderer);
        Assert.That(Red(both), Is.EqualTo(Red(single)).Within(3));
    }

    [Test]
    public void ALayerInsideAClip_IsCutByIt()
    {
        // The layer reaches past its clipping parent's right edge; nothing of it shows there.
        var root = new TestRoot(64, 64);
        var clip = new TestControl { ClipToBounds = true };
        clip.Bounds = new Rect(0, 0, 20, 64);
        clip.RenderSize = new Size<double>(20, 64);
        var group = new TestControl { Opacity = 0.5, IsLayer = true };
        group.Bounds = new Rect(4, 4, 40, 30);
        group.RenderSize = new Size<double>(40, 30);
        var square = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 30, 20)) };
        square.Bounds = new Rect(0, 0, 30, 20);
        square.RenderSize = new Size<double>(30, 20);
        group.Add(square);
        clip.Add(group);
        root.Add(clip);

        var (inside, outside) = Render(root, (8, 14), (28, 14));

        Assert.That(Red(inside), Is.InRange(110, 145), "inside the clip the layer shows");
        Assert.That(Red(outside), Is.LessThan(10), "past it, nothing");
    }

    [Test]
    public void ALayerAwayFromTheCorner_IsNotCutByARoundedClipAboveIt()
    {
        // The rounded clip starts at (16,16) and the layer at (24,24): in the layer's own texture its pixels lie above and
        // left of the clip's corner, in the window well inside it.
        var root = new TestRoot(64, 64);
        var clip = new TestControl { ClipToBounds = true, ClipCornerRadius = new ProceduralGeometry.CornerRadius(4) };
        clip.Bounds = new Rect(16, 16, 48, 48);
        clip.RenderSize = new Size<double>(48, 48);
        var group = new TestControl { Opacity = 0.5, IsLayer = true };
        group.Bounds = new Rect(8, 8, 20, 20);
        group.RenderSize = new Size<double>(20, 20);
        var square = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 20, 20)) };
        square.Bounds = new Rect(0, 0, 20, 20);
        square.RenderSize = new Size<double>(20, 20);
        group.Add(square);
        clip.Add(group);
        root.Add(clip);

        var (corner, middle) = Render(root, (26, 26), (34, 34));

        Assert.Multiple(() =>
        {
            Assert.That(Red(corner), Is.InRange(110, 145), "near the layer's corner");
            Assert.That(Red(middle), Is.InRange(110, 145), "and in its middle");
        });
    }

    [Test]
    public void TextInALayerAwayFromTheCorner_IsNotCutByARoundedClipAboveIt()
    {
        // The text's own shader clips with its own code, so it needs the same check as the shapes.
        var asLayer = LitPixelsOfText(layer: true);
        var plain = LitPixelsOfText(layer: false);

        Assert.That(plain, Is.GreaterThan(100), "the text draws at all");
        Assert.That(asLayer, Is.EqualTo(plain).Within(10).Percent, "and as much of it inside a layer");
    }

    private int LitPixelsOfText(bool layer)
    {
        const int dim = 128;
        var text = new TextBlock { Text = "WWW", Foreground = Brushes.White, FontSize = 30 };
        var group = new Border { IsLayer = layer, Opacity = 0.5, Child = text };
        var clip = new Border
        {
            ClipToBounds = true,
            ClipCornerRadius = new ProceduralGeometry.CornerRadius(4),
            Padding = new Thickness(4),
            Child = group
        };
        var root = new VisualRoot(new Border { Padding = new Thickness(40), Child = clip }, dim, dim);
        ((IMeasurableComponent)root).Measure(new Size<double>(dim, dim));
        ((IMeasurableComponent)root).Arrange(new Rect(0, 0, dim, dim));

        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, dim, dim) { ClearColor = Colors.Black };
        RenderDirty.MarkStructural();
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        using var img = renderer.RenderTarget.ResolveTexture.ReadbackToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        System.Runtime.InteropServices.Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);
        var lit = 0;
        for (var i = 0; i < dim * dim * 4; i += 4)
        {
            if (pixels[i] + pixels[i + 1] + pixels[i + 2] > 150)
            {
                lit++;
            }
        }

        return lit;
    }

    [Test]
    public void ASiblingThatStartsDrawingBeforeALayer_IsPatchedInOutsideIt()
    {
        var stage = new TestControl { Bounds = new Rect(0, 0, 64, 64), RenderSize = new Size<double>(64, 64) };
        var sibling = new TestControl { Bounds = new Rect(0, 40, 64, 20), RenderSize = new Size<double>(64, 20) };
        stage.Add(sibling);
        var group = new TestControl { Opacity = 0.5, IsLayer = true };
        group.Bounds = new Rect(4, 4, 30, 30);
        group.RenderSize = new Size<double>(30, 30);
        var square = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 30, 30)) };
        square.Bounds = new Rect(0, 0, 30, 30);
        square.RenderSize = new Size<double>(30, 30);
        group.Add(square);
        stage.Add(group);
        var root = new VisualRoot(stage, 64, 64);

        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Black };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        sibling.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 64, 20));
        sibling.Invalidate();
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        Assert.That(renderer.Cache.LastFrameReplayed, Is.True, "the sibling is patched in, not walked");
        var (below, layer) = Pixels(renderer, (10, 50), (10, 10));
        Assert.Multiple(() =>
        {
            Assert.That(Blue(below), Is.GreaterThan(240), "the sibling draws on the window at full strength");
            Assert.That(Red(layer), Is.InRange(110, 145), "the layer is still laid down");
        });
    }

    [Test]
    public void ALayerDrawnAsCopies_StillFades()
    {
        // Copies are not drawn through a texture of their own; the fade must still reach the children.
        var (root, group) = Overlap(layer: true);
        group.RenderClones = [Matrix4x4F.Identity];

        var (single, _) = Render(root);

        Assert.That(Red(single), Is.InRange(110, 145), "half red over blue, not opaque");
    }

    [Test]
    public void AChildOutsideTheLayersBounds_IsInTheLayerToo()
    {
        // The group is 20x20 at (4,4); its child sits past its right edge, at (34,4) on the window.
        var root = new TestRoot(64, 64);
        var group = new TestControl { Opacity = 0.5, IsLayer = true };
        group.Bounds = new Rect(4, 4, 20, 20);
        group.RenderSize = new Size<double>(20, 20);
        var inside = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 20, 20)) };
        inside.Bounds = new Rect(0, 0, 20, 20);
        inside.RenderSize = new Size<double>(20, 20);
        var outside = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 20, 20)) };
        outside.Bounds = new Rect(30, 0, 20, 20);
        outside.RenderSize = new Size<double>(20, 20);
        group.Add(inside);
        group.Add(outside);
        root.Add(group);

        var (within, past) = Render(root, (12, 12), (44, 12));

        Assert.Multiple(() =>
        {
            Assert.That(Red(within), Is.InRange(110, 145), "inside the bounds");
            Assert.That(Red(past), Is.EqualTo(Red(within)).Within(3), "and past them, the same");
        });
    }

    [Test]
    public void APlacedGeometry_IsMeasuredWhereItIsDrawn()
    {
        // A 10x10 square scaled by 3 and moved by 4: it covers (4,4)-(34,34) of a 10x10 element at (4,4).
        var root = new TestRoot(64, 64);
        var group = new TestControl { Opacity = 0.5, IsLayer = true };
        group.Bounds = new Rect(4, 4, 10, 10);
        group.RenderSize = new Size<double>(10, 10);
        var placed = Matrix4x4.CreateScale<float>(3f) * Matrix4x4.CreateTranslation<float>(4, 4, 0);
        var icon = new TestControl
        {
            RenderAction = s => s.DrawGeometry(Brushes.Red, new RectangleGeometry(new Rect(0, 0, 10, 10)), null, placed)
        };
        icon.Bounds = new Rect(0, 0, 10, 10);
        icon.RenderSize = new Size<double>(10, 10);
        group.Add(icon);
        root.Add(group);

        var (near, far) = Render(root, (12, 12), (30, 30));

        Assert.Multiple(() =>
        {
            Assert.That(Red(near), Is.InRange(110, 145), "inside the element");
            Assert.That(Red(far), Is.EqualTo(Red(near)).Within(3), "and where the placed square reaches past it");
        });
    }

    [Test]
    public void AStrokeOverTheLayersEdge_IsNotCut()
    {
        // A 10 px pen centered on the group's left edge reaches 5 px outside it.
        var root = new TestRoot(64, 64);
        var group = new TestControl { Opacity = 0.5, IsLayer = true };
        group.Bounds = new Rect(20, 10, 30, 30);
        group.RenderSize = new Size<double>(30, 30);
        var framed = new TestControl
        {
            RenderAction = s => s.DrawRectangle(null, new Rect(0, 0, 30, 30), new Pen(Brushes.Red, 10))
        };
        framed.Bounds = new Rect(0, 0, 30, 30);
        framed.RenderSize = new Size<double>(30, 30);
        group.Add(framed);
        root.Add(group);

        var (outer, inner) = Render(root, (17, 25), (22, 25));

        Assert.Multiple(() =>
        {
            Assert.That(Red(inner), Is.InRange(110, 145), "the stroke inside the edge");
            Assert.That(Red(outer), Is.EqualTo(Red(inner)).Within(3), "and outside it");
        });
    }

    // A 40x20 red bar at (4,4) under a mask; the bar's left edge is x 4, its right edge x 44.
    private static (TestRoot Root, TestControl Bar) Masked(Brush mask)
    {
        var root = new TestRoot(64, 64);
        var bar = new TestControl
        {
            OpacityMask = mask,
            RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 40, 20))
        };
        bar.Bounds = new Rect(4, 4, 40, 20);
        bar.RenderSize = new Size<double>(40, 20);
        root.Add(bar);
        return (root, bar);
    }

    private static LinearGradientBrush FadeIn() => new([
        new GradientStop(Colors.Transparent, 0),
        new GradientStop(Colors.Black, 1)
    ])
    {
        StartPoint = new Vector2<double>(0, 0.5),
        EndPoint = new Vector2<double>(1, 0.5)
    };

    [Test]
    public void ALinearMask_FadesTheElementAcrossItsBox()
    {
        var (left, right) = Render(Masked(FadeIn()).Root, (6, 14), (42, 14));
        var (middle, _) = Render(Masked(FadeIn()).Root, (24, 14), (24, 14));

        Assert.Multiple(() =>
        {
            Assert.That(Red(left), Is.LessThan(30), "almost nothing at the transparent end");
            Assert.That(Red(middle), Is.InRange(110, 145), "half way across, half");
            Assert.That(Red(right), Is.GreaterThan(225), "almost all of it at the opaque end");
        });
    }

    [Test]
    public void AMask_FollowsTheElementsTransform()
    {
        // A 20x10 bar scaled by 2 covers the same 40x20 as the plain one; the mask stretches with it.
        var root = new TestRoot(64, 64);
        var bar = new TestControl
        {
            OpacityMask = FadeIn(),
            RenderTransform = new Transform { ScaleX = 2, ScaleY = 2 },
            RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 20, 10))
        };
        bar.Bounds = new Rect(4, 4, 20, 10);
        bar.RenderSize = new Size<double>(20, 10);
        root.Add(bar);

        var (left, right) = Render(root, (6, 14), (42, 14));

        Assert.Multiple(() =>
        {
            Assert.That(Red(left), Is.LessThan(30), "transparent at the left of the scaled bar");
            Assert.That(Red(right), Is.GreaterThan(225), "opaque at its right");
        });
    }

    [Test]
    public void AMaskOnATurnedElement_FollowsThePerspective()
    {
        // Red left half, green right half, turned about Y around the left edge with perspective, so the middle is pushed
        // in depth; the mask keeps exactly the left half of the element's box, so all the green goes and all the red stays.
        var halfMask = new LinearGradientBrush([
            new GradientStop(Colors.Black, 0),
            new GradientStop(Colors.Black, 0.5),
            new GradientStop(Colors.Transparent, 0.5),
            new GradientStop(Colors.Transparent, 1)
        ])
        {
            StartPoint = new Vector2<double>(0, 0.5),
            EndPoint = new Vector2<double>(1, 0.5)
        };

        var unmasked = CountColors(Turned(null));
        var masked = CountColors(Turned(halfMask));

        Assume.That(unmasked.Red, Is.GreaterThan(80), "the turned bar shows its red half");
        Assume.That(unmasked.Green, Is.GreaterThan(80), "and its green half");
        Assert.Multiple(() =>
        {
            Assert.That(masked.Green, Is.LessThan(unmasked.Green / 10), "the green half is masked away");
            Assert.That(masked.Red, Is.GreaterThan(unmasked.Red * 9 / 10), "the red half is kept");
        });
    }

    private static TestRoot Turned(Brush mask)
    {
        var root = new TestRoot(64, 64);
        var bar = new TestControl
        {
            OpacityMask = mask,
            IsLayer = true,
            RenderTransform = new Transform { RotationY = 45, Perspective = 120, RotationCenterX = 0, RotationCenterY = 10 },
            RenderAction = s => s
                .DrawRectangle(Brushes.Red, new Rect(0, 0, 20, 20))
                .DrawRectangle(Brushes.Lime, new Rect(20, 0, 20, 20))
        };
        bar.Bounds = new Rect(12, 20, 40, 20);
        bar.RenderSize = new Size<double>(40, 20);
        root.Add(bar);
        return root;
    }

    private (int Red, int Green) CountColors(TestRoot root)
    {
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Black };
        Assert.That(renderer.RenderFrame(root), Is.True);
        Assert.That(renderer.RenderFrame(root), Is.True);

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"layer_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var pixels = Image.Load(path).GetPixelBuffer(0, 0);
            var red = 0;
            var green = 0;
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var pixel = pixels.GetPixel<uint>(x, y);
                    if (Red(pixel) > 160 && Green(pixel) < 60)
                    {
                        red++;
                    }
                    else if (Green(pixel) > 160 && Red(pixel) < 60)
                    {
                        green++;
                    }
                }
            }

            return (red, green);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static int Green(uint pixel) => (int)((pixel >> 8) & 0xFF);

    [Test]
    public void AMaskOnAnElementOfNoSize_IsLaidOverWhatItsChildrenDraw()
    {
        var root = new TestRoot(64, 64);
        var holder = new TestControl { OpacityMask = FadeIn() };
        holder.Bounds = new Rect(4, 4, 0, 0);
        holder.RenderSize = new Size<double>(0, 0);
        var bar = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 40, 20)) };
        bar.Bounds = new Rect(0, 0, 40, 20);
        bar.RenderSize = new Size<double>(40, 20);
        holder.Add(bar);
        root.Add(holder);

        var (left, right) = Render(root, (6, 14), (42, 14));

        Assert.Multiple(() =>
        {
            Assert.That(Red(left), Is.LessThan(40), "faded at the left of what the children draw");
            Assert.That(Red(right), Is.GreaterThan(200), "kept at its right");
        });
    }

    [Test]
    public void AGradientMaskWithNoStops_ShowsNothing()
    {
        var (root, _) = Masked(new LinearGradientBrush());

        var (middle, _) = Render(root, (24, 14), (24, 14));

        Assert.That(Red(middle), Is.LessThan(10), "a gradient with nothing in it masks everything away");
    }

    [Test]
    public void ARadialMask_KeepsTheMiddleAndMeltsTheCorners()
    {
        var mask = new RadialGradientBrush([
            new GradientStop(Colors.Black, 0),
            new GradientStop(Colors.Transparent, 1)
        ]);
        var (root, _) = Masked(mask);

        var (middle, corner) = Render(root, (24, 14), (5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(Red(middle), Is.GreaterThan(225), "opaque in the middle");
            Assert.That(Red(corner), Is.LessThan(10), "gone past the ellipse, in the corner");
        });
    }

    [Test]
    public void ASolidMask_FadesByItsAlpha()
    {
        var (root, _) = Masked(new SolidColorBrush(Color.FromRgba(0, 0, 0, 128)));

        var (inside, _) = Render(root, (24, 14), (24, 14));

        Assert.That(Red(inside), Is.InRange(110, 145));
    }

    [Test]
    public void AMask_MakesALayerOfItsOwn()
    {
        // No IsLayer: the mask alone must draw the group as one, so the overlap is a single coat.
        var (root, group) = Overlap(layer: false);
        group.Opacity = 1;
        group.OpacityMask = new SolidColorBrush(Color.FromRgba(0, 0, 0, 128));

        var (single, both) = Render(root);

        Assert.Multiple(() =>
        {
            Assert.That(Red(single), Is.InRange(110, 145));
            Assert.That(Red(both), Is.EqualTo(Red(single)).Within(3), "one coat where the squares overlap");
        });
    }

    [Test]
    public void AMaskChangedInPlace_ShowsOnTheNextFrame()
    {
        var mask = FadeIn();
        var (root, _) = Masked(mask);
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        mask.GradientStops[0].Color = Colors.Black;
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        var (left, _) = Pixels(renderer, (6, 14), (6, 14));
        Assert.That(Red(left), Is.GreaterThan(225), "both ends are opaque now");
    }

    [Test]
    public void ALayerWithinALayer_IsLaidDownOnceInEach()
    {
        // Two nested half-opaque layers over one red square: a quarter of red, the same as two plain nested fades.
        var root = new TestRoot(64, 64);
        var outer = new TestControl { Opacity = 0.5, IsLayer = true };
        outer.Bounds = new Rect(4, 4, 40, 30);
        outer.RenderSize = new Size<double>(40, 30);
        var inner = new TestControl { Opacity = 0.5, IsLayer = true };
        inner.Bounds = new Rect(0, 0, 40, 30);
        inner.RenderSize = new Size<double>(40, 30);
        var square = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 20, 20)) };
        square.Bounds = new Rect(0, 0, 20, 20);
        square.RenderSize = new Size<double>(20, 20);
        inner.Add(square);
        outer.Add(inner);
        root.Add(outer);

        var (red, _) = Render(root, (8, 14), (8, 14));

        Assert.That(Red(red), Is.InRange(55, 75));
    }

    [Test]
    public void ALayersOwnDrawing_IsInTheLayerToo()
    {
        // The group paints a red background and a red square over half of it: one coat of red at half opacity everywhere.
        var root = new TestRoot(64, 64);
        var group = new TestControl
        {
            Opacity = 0.5,
            IsLayer = true,
            RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 40, 30))
        };
        group.Bounds = new Rect(4, 4, 40, 30);
        group.RenderSize = new Size<double>(40, 30);
        var square = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 20, 20)) };
        square.Bounds = new Rect(0, 0, 20, 20);
        square.RenderSize = new Size<double>(20, 20);
        group.Add(square);
        root.Add(group);

        var (under, background) = Render(root, (8, 14), (34, 14));

        Assert.That(Red(under), Is.EqualTo(Red(background)).Within(3));
    }

    [Test]
    public void AStillLayer_IsDrawnOnce_AndThenLaidDownFromItsTexture()
    {
        var (root, _) = Overlap(layer: true);
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.Cache.LastDrawPath, Is.EqualTo(DrawPath.Replay));
            Assert.That(renderer.Cache.LayersRedrawn, Is.Zero, "nothing in it changed, so nothing is drawn into it");
            Assert.That(renderer.Cache.LayersReused, Is.EqualTo(1), "and its texture is laid down as it is");
        });
        var (single, both) = Pixels(renderer);
        Assert.Multiple(() =>
        {
            Assert.That(Red(single), Is.InRange(110, 145));
            Assert.That(Red(both), Is.EqualTo(Red(single)).Within(3));
        });
    }

    [Test]
    public void ALayersOwnFade_IsPlayedOnItsTexture_WithoutDrawingItAgain()
    {
        var (root, group) = Overlap(layer: true);
        group.BeginAnimation(Adamantium.UI.Controls.Base.UIComponent.OpacityProperty,
            new DoubleAnimation { From = 0.1, To = 0.9, Duration = TimeSpan.FromSeconds(2) });
        var entry = Compositor.EntryFor(group);
        Compositor.StartPending();

        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);
        var early = Pixels(renderer);

        WaitForElapsed(entry, 1.5);
        Assert.That(renderer.RenderFrame(root), Is.True);
        var later = Pixels(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.Cache.LayersReused, Is.EqualTo(1), "the fade is the layer's own, laid down from its texture");
            Assert.That(Red(later.Single), Is.GreaterThan(Red(early.Single) + 40), "and the layer still grew more opaque");
        });
    }

    [Test]
    public void AChildFadingInsideTheLayer_DrawsItAgainEachFrame()
    {
        var (root, group) = Overlap(layer: true);
        var child = (TestControl)System.Linq.Enumerable.First(group.VisualChildren);
        child.BeginAnimation(Adamantium.UI.Controls.Base.UIComponent.OpacityProperty,
            new DoubleAnimation { From = 0.1, To = 1.0, Duration = TimeSpan.FromSeconds(2) });
        var entry = Compositor.EntryFor(child);
        Assume.That(entry?.Channel, Is.EqualTo(CompositorChannel.Opacity), "the child's fade is composited");
        Compositor.StartPending();

        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);
        var early = Pixels(renderer);

        WaitForElapsed(entry, 1.5);
        Assert.That(renderer.RenderFrame(root), Is.True);
        var later = Pixels(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.Cache.LayersRedrawn, Is.EqualTo(1), "a child changed, so the layer is drawn again");
            Assert.That(Red(later.Single), Is.GreaterThan(Red(early.Single) + 30), "and shows the child's new fade");
        });
    }

    private static void WaitForElapsed(Compositor.Entry entry, double seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds + 10);
        while (entry.Elapsed < seconds)
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "the animation's clock runs");
            System.Threading.Thread.Sleep(5);
        }
    }

    [Test]
    public void AnInnerLayersMaskRepainted_DrawsTheOuterLayerAgain()
    {
        var mask = new SolidColorBrush(Colors.Black);
        var root = new TestRoot(64, 64);
        var outer = new TestControl { IsLayer = true };
        outer.Bounds = new Rect(4, 4, 40, 30);
        outer.RenderSize = new Size<double>(40, 30);
        var inner = new TestControl { OpacityMask = mask };
        inner.Bounds = new Rect(0, 0, 20, 20);
        inner.RenderSize = new Size<double>(20, 20);
        var square = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 20, 20)) };
        square.Bounds = new Rect(0, 0, 20, 20);
        square.RenderSize = new Size<double>(20, 20);
        inner.Add(square);
        outer.Add(inner);
        root.Add(outer);

        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Blue };
        for (var frame = 0; frame < 3; frame++)
        {
            Assert.That(renderer.RenderFrame(root), Is.True);
            RenderDirty.Clear();
        }

        mask.Color = Color.FromRgba(0, 0, 0, 128);
        for (var frame = 0; frame < 3; frame++)
        {
            Assert.That(renderer.RenderFrame(root), Is.True);
            RenderDirty.Clear();
        }

        var (inside, _) = Pixels(renderer, (12, 12), (12, 12));
        Assert.That(Red(inside), Is.InRange(110, 145), "the inner layer's new mask shows through the outer one");
    }

    [Test]
    public void AChildRepaintedInPlace_DrawsTheLayerAgain()
    {
        var paint = new SolidColorBrush(Colors.Red);
        var root = new TestRoot(64, 64);
        var group = new TestControl { Opacity = 0.5, IsLayer = true };
        group.Bounds = new Rect(4, 4, 40, 30);
        group.RenderSize = new Size<double>(40, 30);
        var square = new TestControl { RenderAction = s => s.DrawRectangle(paint, new Rect(0, 0, 20, 20)) };
        square.Bounds = new Rect(0, 0, 20, 20);
        square.RenderSize = new Size<double>(20, 20);
        group.Add(square);
        root.Add(group);

        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Black };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        paint.Color = Colors.Lime;
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);

        var (inside, _) = Pixels(renderer, (8, 14), (8, 14));
        Assert.Multiple(() =>
        {
            Assert.That(Green(inside), Is.InRange(110, 145), "the new color shows, at the layer's opacity");
            Assert.That(Red(inside), Is.LessThan(20), "the old one is gone");
        });
    }

    [Test]
    public void ALayerWithChangingPaint_IsNeverLaidDownFromAnOldTexture()
    {
        // A noise brush moves with time on its own; nothing marks the element when it does.
        var (root, group) = Overlap(layer: true);
        var noisy = new TestControl
        {
            RenderAction = s => s.DrawRectangle(new NoiseBrush(), new Rect(0, 0, 10, 10))
        };
        noisy.Bounds = new Rect(30, 0, 10, 10);
        noisy.RenderSize = new Size<double>(10, 10);
        group.Add(noisy);

        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.Cache.LayersReused, Is.Zero);
            Assert.That(renderer.Cache.LayersRedrawn, Is.EqualTo(1));
        });
    }

    [Test]
    public void ALayersFade_IsPlayedByTheRenderThread()
    {
        // A composited opacity animation on the layer: the frames change while the loop thread never ticks.
        var (root, group) = Overlap(layer: true);
        group.BeginAnimation(Adamantium.UI.Controls.Base.UIComponent.OpacityProperty,
            new DoubleAnimation { From = 0.1, To = 0.9, Duration = TimeSpan.FromSeconds(2) });
        var entry = Compositor.EntryFor(group);
        Assert.That(entry?.Channel, Is.EqualTo(CompositorChannel.Opacity));
        Compositor.StartPending();

        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);
        var early = Pixels(renderer);

        while (entry.Elapsed < 1.5) System.Threading.Thread.Sleep(5);
        Assert.That(renderer.RenderFrame(root), Is.True);
        var later = Pixels(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(Red(later.Single), Is.GreaterThan(Red(early.Single) + 40), "the layer grew more opaque");
            Assert.That(Red(later.Both), Is.EqualTo(Red(later.Single)).Within(3), "and stayed one coat where they overlap");
        });
    }

    [Test]
    public void ALayerAfterASiblingOfTheSameShapes_DrawsItsOwnIntoTheLayer()
    {
        // The sandbox stand: a plain group of circles, then a layered one. Their instances share a batch family, and the
        // layer's must still be drawn between its marks, not with the sibling's.
        var root = new TestRoot(128, 64);
        foreach (var (x, layer) in new[] { (4.0, false), (68.0, true) })
        {
            var group = new TestControl { Opacity = 0.5, IsLayer = layer };
            group.Bounds = new Rect(x, 4, 56, 40);
            group.RenderSize = new Size<double>(56, 40);
            foreach (var dx in new[] { 0.0, 16.0 })
            {
                var circle = new TestControl { RenderAction = s => s.DrawEllipse(new Rect(0, 0, 32, 32), Brushes.Red, 0, 360, Adamantium.ProceduralGeometry.Shapes.EllipseType.Sector, null, 0) };
                circle.Bounds = new Rect(dx, 0, 32, 32);
                circle.RenderSize = new Size<double>(32, 32);
                group.Add(circle);
            }

            root.Add(group);
        }

        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 128, 64) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);
        var (single, both) = Pixels(renderer, (68 + 6, 20), (68 + 24, 20));

        Assert.Multiple(() =>
        {
            Assert.That(Red(single), Is.InRange(110, 145), "the layer shows");
            Assert.That(Red(both), Is.EqualTo(Red(single)).Within(3), "as one coat");
        });
    }

    [Test]
    public void ALayerInsideAMovingNode_StillHoldsItsChildren()
    {
        // A scroll area moves its content as one node; the layer inside it must still draw its children into itself.
        var (root, group) = Overlap(layer: true);
        root.Remove(group);
        var node = new TestControl { IsRenderMotionNode = true };
        node.Bounds = new Rect(0, 0, 64, 64);
        node.RenderSize = new Size<double>(64, 64);
        node.Add(group);
        root.Add(node);

        var (single, both) = Render(root);

        Assert.Multiple(() =>
        {
            Assert.That(Red(single), Is.InRange(110, 145), "the layer shows");
            Assert.That(Red(both), Is.EqualTo(Red(single)).Within(3), "as one coat");
        });
    }

    [Test]
    public void WithoutALayer_TheFadeReachesEachChildOnItsOwn()
    {
        // The cheap path, unchanged: each square is faded separately, so the overlap is denser.
        var (single, both) = Render(Overlap(layer: false).Root);

        Assert.That(Red(both), Is.GreaterThan(Red(single) + 30));
    }

    private sealed class StubResourceFactory : IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        private FontRenderer _fontRenderer;

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, System.Collections.Generic.IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => _fontRenderer ??= new FontRenderer(graphicsDevice);
    }
}
