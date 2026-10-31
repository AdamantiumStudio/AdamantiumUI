using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.TypeParsers;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A pure move costs a patch, and the patched picture equals a full walk.
[TestFixture]
[Category("Gpu")]
public class MovedElementPatchRenderTests
{
    private const int Dim = 96;

    [TearDown]
    public void TearDown() => AnimationManager.Reset();

    private sealed class Scene : IDisposable
    {
        public OffscreenTestRenderer Renderer;
        public VisualRoot Root;
        public TestControl Mover;      // the thumb: moves, draws, clips nothing
        public TestControl Rider;      // its child - it has its OWN slot holding its FULL world, so it only follows
                                       // if the move is carried down the subtree

        public void Draw()
        {
            Assert.That(Renderer.RenderFrame(Root), Is.True, "off-screen frame must render");
            RenderDirty.Clear();
        }

        public void Dispose() => Renderer.Dispose();
    }

    private static TestControl Placed(Rect bounds) =>
        new() { Bounds = bounds, RenderSize = new Size<double>(bounds.Width, bounds.Height) };

    // A still background plus a two-level mover. The background is what makes the assertion about the SCENE rather than
    // about the mover: a walk would redraw it too, so "patched and identical" is the whole claim.
    private static Scene NewScene()
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        for (var i = 0; i < 4; i++)
        {
            var still = Placed(new Rect(0, i * 24, Dim, 20));
            still.RenderAction = s => s.DrawRectangle(Brushes.Green, new Rect(0, 0, Dim, 20));
            stage.Add(still);
        }

        var mover = Placed(new Rect(8, 8, 24, 24));
        mover.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 24, 24));
        stage.Add(mover);

        var rider = Placed(new Rect(4, 4, 8, 8));
        rider.RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 8, 8));
        mover.Add(rider);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Mover = mover, Rider = rider };
        scene.Draw();
        return scene;
    }

    // A tab's close glyph: two strokes of one path, as its template draws it.
    private static Geometry Cross() => new GeometryParser().Parse("M0,0 L8,8 M8,0 L0,8");

    private static byte[] Pixels(OffscreenTestRenderer renderer)
    {
        using var img = renderer.RenderTarget.ResolveTexture.ToImage();
        var bytes = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    private static int DifferingPixels(byte[] a, byte[] b)
    {
        var count = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3]) count++;
        }

        return count;
    }

    private static void AssertMatchesAFullWalk(Scene scene, byte[] patched, string because)
    {
        RenderDirty.MarkStructural();
        scene.Draw();

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.False, "the reference frame has to actually walk");
        Assert.That(DifferingPixels(patched, Pixels(scene.Renderer)), Is.Zero, because);
    }

    // THE DRAG. One element changes place; nothing else about the scene changes.
    [Test]
    public void MovingOneElement_PatchesTheFrame_AndLandsWhereAWalkPutsIt()
    {
        using var scene = NewScene();

        scene.Mover.Bounds = new Rect(48, 8, 24, 24);
        scene.Draw();

        var patched = Pixels(scene.Renderer);
        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True,
            "a move is a slot write - it must not cost the whole scene a walk");
        AssertMatchesAFullWalk(scene, patched, "the moved element must be drawn where a full walk draws it");
    }

    // The subtree. A motion node moves its descendants by ONE matrix; an ordinary mover cannot - every element under it
    // holds its own slot carrying its own full world, and each has to be written.
    [Test]
    public void MovingAContainer_CarriesItsChildrenWithIt()
    {
        using var scene = NewScene();

        scene.Mover.Bounds = new Rect(8, 56, 24, 24);
        scene.Draw();

        var patched = Pixels(scene.Renderer);
        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "the frame must still patch");
        AssertMatchesAFullWalk(scene, patched, "the child must have moved with its parent, not stayed behind");
    }

    // Repeated steps, as a drag actually arrives - each frame patching on top of the one before it. A slot written from a
    // STALE world memo would drift, and only a sequence shows that.
    [Test]
    public void DraggingItStepByStep_PatchesEveryFrame()
    {
        using var scene = NewScene();

        for (var step = 1; step <= 6; step++)
        {
            scene.Mover.Bounds = new Rect(8 + step * 8, 8, 24, 24);
            scene.Draw();
            Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, $"drag step {step} must patch, not walk");
        }

        AssertMatchesAFullWalk(scene, Pixels(scene.Renderer), "six patched steps must end where one walk would put it");
    }

    // A dragged tab: its RenderTransform moves it every frame, and its close button is a stroke - a per-unit draw beside
    // its batched fill. Every frame must have both where a walk puts them, not the stroke a few frames behind.
    [TestCase(false)]
    [TestCase(true)]
    public void DraggingByRenderTransform_TakesItsStrokesAlong_EveryFrame(bool motionNode)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var tab = Placed(new Rect(4, 8, 32, 24));
        tab.IsRenderMotionNode = motionNode;
        var transform = new Transform();
        tab.RenderTransform = transform;
        tab.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 32, 24));
        stage.Add(tab);

        var pen = new Pen(Brushes.Red, 2);
        var cross = Cross();
        var glyph = Placed(new Rect(20, 6, 10, 10));
        glyph.RenderAction = s =>
            s.DrawGeometry(null, cross, pen);
        tab.Add(glyph);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Mover = tab, Rider = glyph };
        scene.Draw();

        for (var step = 1; step <= 6; step++)
        {
            transform.TranslateX = step * 9;
            scene.Draw();
            AssertMatchesAFullWalk(scene, Pixels(renderer), $"drag step {step}: the stroke must be where the fill is");
        }
    }

    // A dragged tab whose button lights up under the pointer: frames that only move, only light up, or both, some of them
    // two recordings drawn at once. The hover is what a patch is for, and the move must not be lost to it.
    [TestCase(false)]
    [TestCase(true)]
    public void DraggingByRenderTransform_WhileItsButtonLightsUp_TakesTheButtonAlong(bool backgroundAppears)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var tab = Placed(new Rect(4, 8, 32, 24));
        var transform = new Transform();
        tab.RenderTransform = transform;
        tab.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 32, 24));
        stage.Add(tab);

        var stroke = new SolidColorBrush(Colors.Red);
        var pen = new Pen(stroke, 2);
        var cross = Cross();
        var lit = false;
        var grid = Placed(new Rect(0, 0, 32, 24));
        tab.Add(grid);
        var button = Placed(new Rect(18, 4, 14, 14));
        button.RenderAction = s =>
        {
            if (backgroundAppears && lit)
            {
                s.DrawRectangle(Brushes.Yellow, new Rect(0, 0, 14, 14));
            }
        };
        grid.Add(button);
        var glyph = Placed(new Rect(2, 2, 10, 10));
        glyph.RenderAction = s =>
            s.DrawGeometry(null, cross, pen);
        button.Add(glyph);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Mover = tab, Rider = button };
        scene.Draw();

        for (var step = 1; step <= 12; step++)
        {
            var moves = step % 3 != 2;
            var lights = step % 3 != 1;
            if (moves)
            {
                transform.TranslateX = step * 4;
            }

            if (lights)
            {
                lit = !lit;
                if (backgroundAppears)
                {
                    button.InvalidateRender(false);
                }
                else
                {
                    stroke.Color = lit ? Colors.White : Colors.Red;
                }
            }

            if (step % 2 == 0)
            {
                // The loop recorded a frame the render thread had not drawn yet: two packets are applied for one draw.
                renderer.Cache.RecordFrame(scene.Root);
                RenderDirty.Clear();
                transform.TranslateX += 2;
            }

            scene.Draw();
            AssertMatchesAFullWalk(scene, Pixels(renderer), $"drag step {step}: the button must be where the tab is");
        }
    }

    // The same tab after a slide of its own - a reorder settling it - which the compositor ran and the drag cancelled.
    [Test]
    public void DraggingByRenderTransform_AfterACompositedSlide_TakesItsStrokesAlong()
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var tab = Placed(new Rect(4, 8, 32, 24));
        var transform = new Transform();
        tab.RenderTransform = transform;
        tab.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 32, 24));
        stage.Add(tab);

        var pen = new Pen(Brushes.Red, 2);
        var cross = Cross();
        var glyph = Placed(new Rect(20, 6, 10, 10));
        glyph.RenderAction = s =>
            s.DrawGeometry(null, cross, pen);
        tab.Add(glyph);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Mover = tab, Rider = glyph };
        scene.Draw();

        transform.BeginAnimation(Transform.TranslateXProperty,
            new DoubleAnimation { From = 0, To = 9, Duration = TimeSpan.FromSeconds(1) });
        Compositor.StartPending();
        Assert.That(Compositor.EntryFor(transform), Is.Not.Null, "the slide must be composited");
        scene.Draw();

        transform.CancelAnimation(Transform.TranslateXProperty);
        for (var step = 1; step <= 6; step++)
        {
            transform.TranslateX = step * 9;
            scene.Draw();
            AssertMatchesAFullWalk(scene, Pixels(renderer), $"drag step {step}: the stroke must be where the fill is");
        }
    }

    // THE DROP GAP. A tile inside a scrolling panel is batched NODE-RELATIVE: its place is in the instance, not in a
    // slot of its own, so writing slots moves everything EXCEPT it. That is exactly what the drag-and-drop demo showed -
    // the labels (per-unit draws, re-pointed at replay) slid to the new slot and the tiles stayed behind. Re-baking the
    // moved subtree answers both, and this is the test that tells them apart.
    [Test]
    public void MovingSomethingInsideAMotionNode_TakesItsBatchedTileWithIt()
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var panel = Placed(new Rect(0, 0, Dim, Dim));
        panel.IsRenderMotionNode = true;      // the scrolling host: its subtree rides ITS slot
        stage.Add(panel);

        var still = Placed(new Rect(0, 60, Dim, 20));
        still.RenderAction = s => s.DrawRectangle(Brushes.Green, new Rect(0, 0, Dim, 20));
        panel.Add(still);

        var container = Placed(new Rect(8, 8, 24, 24));
        var tile = Placed(new Rect(0, 0, 24, 24));
        tile.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 24, 24));
        container.Add(tile);
        panel.Add(container);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Mover = container, Rider = tile };
        scene.Draw();

        container.Bounds = new Rect(48, 8, 24, 24);   // the gap opens: the container slides inside the node
        scene.Draw();

        var patched = Pixels(renderer);
        Assert.That(renderer.Cache.LastFrameReplayed, Is.True, "a tile sliding inside a scrolling panel must still patch");
        AssertMatchesAFullWalk(scene, patched, "the TILE must have moved, not only what is drawn per unit beside it");
    }

    // THE TAB TRANSITION. A slide moves a whole view rigidly, and every view worth sliding has a scroll area somewhere
    // inside it - so "anything under the mover clips" refused every single frame of every switch. A recorded Scissor is
    // the one world-space rect in the stream; it is derived again now instead of costing a re-record, and this asserts
    // the derivation actually lands (a stale one leaves the clipped band behind while its content moves).
    [Test]
    public void MovingAMotionNodeThatClips_TakesItsViewportWithIt()
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var node = Placed(new Rect(4, 4, 40, 40));
        node.IsRenderMotionNode = true;          // the sliding view
        stage.Add(node);

        var viewport = Placed(new Rect(0, 0, 24, 24));
        viewport.ClipToBounds = true;            // the scroll area inside it
        node.Add(viewport);

        var content = Placed(new Rect(0, 0, 80, 80));   // bigger than the viewport, so the clip is visible at all
        content.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 80, 80));
        viewport.Add(content);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Mover = node, Rider = content };
        scene.Draw();

        node.Bounds = new Rect(40, 40, 40, 40);
        scene.Draw();

        var patched = Pixels(renderer);
        Assert.That(renderer.Cache.LastFrameReplayed, Is.True,
            "a rigid slide is one matrix write - a clip inside it must not cost the window a re-record");
        AssertMatchesAFullWalk(scene, patched, "the clipped band must have moved with its viewport");
    }

    // A sliding node containing another motion node: the inner node's slot is written too. Asserts the picture only; such
    // frames may walk.
    [Test]
    public void MovingAMotionNodeThatContainsAnother_CarriesTheInnerOne()
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var outer = Placed(new Rect(4, 4, 60, 60));
        outer.IsRenderMotionNode = true;
        stage.Add(outer);

        var inner = Placed(new Rect(2, 2, 40, 40));
        inner.IsRenderMotionNode = true;
        outer.Add(inner);

        var tile = Placed(new Rect(0, 0, 20, 20));
        tile.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 20, 20));
        inner.Add(tile);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Mover = outer, Rider = tile };
        scene.Draw();

        outer.Bounds = new Rect(40, 40, 60, 60);
        scene.Draw();

        AssertMatchesAFullWalk(scene, Pixels(renderer), "the inner node's subtree must have moved with the outer one");
    }

    // A sliding view containing a rotated unit (own slot, full world): the straggler is re-baked beside the node's slot
    // write instead of refusing the patch.
    [Test]
    public void MovingAMotionNodeWithRotatedContent_CarriesItInstead_OfRefusingTheFrame()
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var view = Placed(new Rect(0, 0, Dim, Dim));
        view.IsRenderMotionNode = true;      // the sliding view
        stage.Add(view);

        var upright = Placed(new Rect(4, 4, 24, 24));
        upright.RenderAction = s => s.DrawRectangle(Brushes.Green, new Rect(0, 0, 24, 24));
        view.Add(upright);

        // The turned label. Its world under the node is sheared, so it holds its own slot rather than the node's.
        var turned = Placed(new Rect(40, 40, 24, 24));
        turned.RenderTransform = new Transform { RotationAngle = 30 };
        turned.RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 24, 24));
        view.Add(turned);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Mover = view, Rider = turned };
        scene.Draw();

        view.Bounds = new Rect(20, 12, Dim, Dim);   // the slide
        scene.Draw();

        var patched = Pixels(renderer);
        Assert.That(renderer.Cache.LastFrameReplayed, Is.True,
            "one turned label must not cost the whole slide a walk of the scene");
        AssertMatchesAFullWalk(scene, patched, "the turned label must have slid with the view, not stayed behind");
    }

    // A mover with clips below it patches: scissors are re-derived for Scissor ops, batch segments and instanced flushes.
    [Test]
    public void AMoverThatClips_PatchesAndTakesItsViewportWithIt()
    {
        using var scene = NewScene();
        scene.Mover.ClipToBounds = true;
        RenderDirty.MarkStructural();
        scene.Draw();

        scene.Mover.Bounds = new Rect(48, 8, 24, 24);
        scene.Draw();

        var patched = Pixels(scene.Renderer);
        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True,
            "a clipping mover must not cost the frame a walk - its scissors are derived again");
        AssertMatchesAFullWalk(scene, patched, "the moved viewport must clip where a full walk clips it");
    }

    // A drag is not only a move. The slider's accent fill and both halves of its track RESIZE on every step, and a size
    // lives in the drawn payload rather than in the matrix - so this one is forgiven on a condition the move does not
    // need: the element must be re-baked on the same frame. It always is (arrange marks a resized element
    // geometry-invalid), and the assertion against the walk is what proves the new size actually reached the picture.
    [Test]
    public void ResizingOneElement_PatchesTheFrame_AndIsDrawnAtItsNewSize()
    {
        using var scene = NewScene();

        scene.Mover.Bounds = new Rect(8, 8, 56, 24);
        scene.Mover.RenderSize = new Size<double>(56, 24);
        scene.Mover.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 56, 24));
        scene.Mover.Invalidate();
        scene.Draw();

        var patched = Pixels(scene.Renderer);
        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True,
            "a resize that the patch is already re-baking must not also cost the scene a walk");
        AssertMatchesAFullWalk(scene, patched, "the element must be drawn at its NEW size, not its old one");
    }

    // A motion node inside an ordinary mover's subtree announces no move, yet its slot must be rewritten too.
    [Test]
    public void MovingAContainer_CarriesAMotionNodeInsideIt()
    {
        using var scene = NewScene();

        // Promote the rider and let a walk record the scene in its new shape, so what follows is a plain MOVE.
        scene.Rider.IsRenderMotionNode = true;
        RenderDirty.MarkStructural();
        scene.Draw();

        scene.Mover.Bounds = new Rect(48, 56, 24, 24);
        scene.Draw();

        var patched = Pixels(scene.Renderer);
        AssertMatchesAFullWalk(scene, patched,
            "the motion node under the mover must be carried - its slot is the only thing that moves its subtree");
    }

    // A tab strip under its edge fade: a layer away from the window's corner, panned, holding a translucent close cross
    // over another translucent stroke. Both strokes borrow depth as scratch, so each has to reset its own bounds in the
    // LAYER's pixels, not the window's.
    [TestCase(false)]
    [TestCase(true)]
    public void ACrossInAPannedStripUnderAMask_IsDrawnWhole(bool revealAfterThePan)
    {
        using var reference = StripScene(masked: false, revealAfterThePan: false);
        reference.Mover.Bounds = new Rect(-12, 0, 160, 32);
        reference.Draw();

        using var scene = StripScene(masked: true, revealAfterThePan);
        scene.Mover.Bounds = new Rect(-12, 0, 160, 32);
        scene.Draw();
        if (revealAfterThePan)
        {
            scene.Rider.Visibility = Visibility.Visible;
            scene.Draw();
        }

        scene.Draw();
        scene.Draw();

        var whole = BrightPixels(Pixels(reference.Renderer));
        Assert.That(whole, Is.GreaterThan(0), "the cross shows without the mask");
        Assert.That(BrightPixels(Pixels(scene.Renderer)), Is.EqualTo(whole), "and as much of it under the mask");
        AssertMatchesAFullWalk(scene, Pixels(scene.Renderer), "the panned strip must be laid down as a walk draws it");
    }

    private static Scene StripScene(bool masked, bool revealAfterThePan)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var strip = Placed(new Rect(12, 40, 80, 32));
        strip.ClipToBounds = true;
        if (masked)
        {
            strip.OpacityMask = new LinearGradientBrush([
                new GradientStop(Colors.Black, 0),
                new GradientStop(Colors.Black, 0.7),
                new GradientStop(Colors.Transparent, 1)
            ]) { StartPoint = new Vector2<double>(0, 0.5), EndPoint = new Vector2<double>(1, 0.5) };
        }

        stage.Add(strip);

        var node = Placed(new Rect(0, 0, 160, 32));
        node.IsRenderMotionNode = true;
        strip.Add(node);

        var tab = Placed(new Rect(16, 0, 48, 32));
        tab.RenderAction = s => s.DrawRectangle(Brushes.Gray, new Rect(0, 0, 48, 32), new CornerRadius(4));
        node.Add(tab);

        var band = new SVGParser().Parse("M0,12 L24,12");
        var button = Placed(new Rect(20, 4, 24, 24));
        button.RenderAction = s => s.DrawGeometry(null, band, new Pen(new SolidColorBrush(Color.FromRgba(128, 128, 128, 200)), 24));
        button.Visibility = revealAfterThePan ? Visibility.Hidden : Visibility.Visible;
        tab.Add(button);

        var cross = new SVGParser().Parse("M0,0 L8,8 M8,0 L0,8");
        var glyph = Placed(new Rect(7.5, 7.5, 9, 9));
        glyph.RenderAction = s => s.DrawGeometry(null, cross, new Pen(new SolidColorBrush(Color.FromRgba(255, 255, 255, 200)), 1));
        button.Add(glyph);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Mover = node, Rider = button };
        scene.Draw();
        return scene;
    }

    private static int BrightPixels(byte[] pixels)
    {
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 160 && pixels[i + 1] > 160 && pixels[i + 2] > 160) count++;
        }

        return count;
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
