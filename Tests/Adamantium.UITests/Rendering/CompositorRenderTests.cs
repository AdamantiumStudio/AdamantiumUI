using System;
using System.Threading;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Rendering;
using Adamantium.UI.Rendering.RenderUnits;
using Adamantium.Vulkan.Core;
using NUnit.Framework;
using Image = Adamantium.Imaging.Image;

namespace Adamantium.UITests.Rendering;

// The claim the compositor is FOR, tested where it can't be faked: on the GPU, with the loop thread doing nothing at all.
//
// Every headless test of the compositor can only show that some numbers changed. This one shows that the PIXELS moved - the
// production RenderCache, the real transform table, the retained instances - while AnimationManager was never ticked once.
// That is the whole promise: a theme cascade holds the loop thread for a second, and the spinner keeps turning anyway.
[TestFixture]
[Category("Gpu")]
public class CompositorRenderTests
{
    private IGraphicsDevice _device;

    [SetUp]
    public void SetUp() => _device = GpuTestDevice.Device;

    [TearDown]
    public void TearDown() => AnimationManager.Reset();

    // A cache that draws only when asked - the one every snapshot shares - has no next frame soon, so whatever it remembers
    // about the frame it just drew outlives the tree it drew. It kept the elements the compositor moved, and held a page
    // that had long been thrown away.
    [Test]
    public void ACacheRemembersNothingOfAMovedElementOnceItsFrameAndUnitsAreDone()
    {
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64);

        var root = new TestRoot(64, 64);
        var control = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 16, 16)) };
        control.Bounds = new Rect(0, 24, 16, 16);
        control.RenderSize = new Size(16, 16);
        var transform = new Transform();
        control.RenderTransform = transform;
        root.Add(control);

        new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            KeyFrames =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter("TranslateX", 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter("TranslateX", 32.0) } }
            }
        }.Apply(transform);
        Compositor.StartPending();

        Assert.That(renderer.RenderFrame(root), Is.True);
        Assert.That(Held(renderer.Cache, "_compositedFallback"), Is.EqualTo(1), "the frame must have composited the slide");
        Assert.That(Held(renderer.Cache, "_compositedOwners"), Is.Zero, "what moved THIS frame is forgotten with the frame");
        Assert.That(Held(renderer.Cache, "_compositedBuf"), Is.Zero, "...and so is what played");

        renderer.Cache.DisposeUnits();

        Assert.That(Held(renderer.Cache, "_compositedFallback"), Is.Zero, "...and the rest goes with the tree's units");
    }

    private static int Held(RenderCache cache, string field)
    {
        var held = typeof(RenderCache)
            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(cache);
        return (int)held.GetType().GetProperty("Count").GetValue(held);
    }

    [Test]
    public void TheRenderThreadMovesTheElementWhileTheLoopThreadDoesNothing()
    {
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.CornflowerBlue };

        // A 16x16 red square at the left edge, which the animation slides 32 px to the right over one second.
        var root = new TestRoot(64, 64);
        var control = new TestControl
        {
            RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 16, 16))
        };
        control.Bounds = new Rect(0, 24, 16, 16);
        control.RenderSize = new Size(16, 16);

        var transform = new Transform();
        control.RenderTransform = transform;
        root.Add(control);

        var slide = new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            KeyFrames =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter("TranslateX", 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter("TranslateX", 32.0) } }
            }
        };
        slide.Apply(transform);
        Compositor.StartPending();   // the frame that started it is recorded: its clock starts

        Assert.That(Compositor.EntryFor(transform), Is.Not.Null, "the slide must be composited");

        Assert.That(renderer.RenderFrame(root), Is.True);
        var first = Sample(renderer, out var bg);

        // The loop thread is STALLED here: no AnimationManager.Tick, no layout, no re-record of our own doing. Only time
        // passes - and the only thing that may act on it is the render thread.
        Thread.Sleep(400);

        Assert.That(renderer.RenderFrame(root), Is.True);
        var second = Sample(renderer, out _);

        Assert.That(first.Left, Is.Not.EqualTo(bg), "the square starts at the left");
        Assert.That(first.Right, Is.EqualTo(bg), "...and nothing is on the right yet");

        Assert.That(second.Right, Is.Not.EqualTo(bg),
            "after 400 ms the render thread must have slid the square right - with the loop thread never ticked");

        // The square has visibly moved ~13 px, yet the PROPERTY is still where the animation started (it is written only by
        // a loop tick, and there was none). So nothing but the compositor can have moved those pixels - which is the claim.
        Assert.That(transform.TranslateX, Is.LessThan(1.0),
            "the property must NOT have advanced: only the render thread moved the square");
    }

    [Test]
    public void ItTurnsTheElementAboutItsOwnCenter_NotTheWindowOrigin()
    {
        // The compositor composes the element's matrix ITSELF - render transform about the origin, then the layout offset.
        // Get that composition wrong and the spinner does not merely stutter: it flies to the corner of the window, or turns
        // about a point it does not own. A rotating BAR pins it - a square would look the same whatever it turned about.
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.CornflowerBlue };

        var root = new TestRoot(64, 64);
        var control = new TestControl
        {
            RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 24, 8)),
            RenderTransformOrigin = new Vector2(0.5, 0.5)   // its own center
        };
        control.Bounds = new Rect(20, 28, 24, 8);           // a 24x8 bar centered at (32,32) - NOT at the window origin
        control.RenderSize = new Size(24, 8);

        var transform = new Transform();
        control.RenderTransform = transform;
        root.Add(control);

        // A full turn every FOUR seconds, so a quarter turn is a whole second away - far longer than a frame takes to
        // render and read back. The wait below then lands on the angle itself rather than on a hoped-for stopwatch reading.
        new Animation
        {
            Duration = TimeSpan.FromSeconds(4),
            IterationCount = double.PositiveInfinity,
            KeyFrames =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter("RotationAngle", 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter("RotationAngle", 360.0) } }
            }
        }.Apply(transform);
        Compositor.StartPending();

        // Horizontal: (22,32) is inside the bar, (32,22) is above it.
        Assert.That(renderer.RenderFrame(root), Is.True);
        var flat = Probe(renderer, out var bg);
        Assert.That(flat.LeftOfCenter, Is.Not.EqualTo(bg), "the bar starts horizontal");
        Assert.That(flat.AboveCenter, Is.EqualTo(bg));

        // Wait for the ANGLE, not for a duration. Rendering a frame and reading it back costs a couple of hundred
        // milliseconds here, and pinning the test to a sleep instead of to the thing it asserts about is how a test starts
        // failing on a slower machine for no reason. The loop thread is still never ticked.
        var entry = Compositor.EntryFor(transform);
        while (entry.Elapsed < 1.0) Thread.Sleep(5);   // a quarter of a four-second turn = 90 degrees

        Assert.That(renderer.RenderFrame(root), Is.True);
        var upright = Probe(renderer, out _);
        Assert.That(upright.AboveCenter, Is.Not.EqualTo(bg), "a quarter turn later the bar stands upright");
        Assert.That(upright.LeftOfCenter, Is.EqualTo(bg), "...and no longer reaches to its left");
    }

    [Test]
    public void TheRenderThreadRepaintsABrushOpacityWhileTheLoopThreadDoesNothing()
    {
        // The skeleton pulse, proven on the GPU: a shared brush's Opacity animates, and the units painting with it must be
        // re-baked - all on the render thread. Nothing about the element changes; the color lives entirely in the brush.
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.CornflowerBlue };

        // A wide opacity swing so the blended pixel shifts visibly; a 4-second period so a frame's render+readback (~200 ms)
        // is a small fraction of it and the two samples land at clearly different opacities.
        var brush = new SolidColorBrush(Colors.Red) { Opacity = 0.1 };
        var root = new TestRoot(64, 64);
        var control = new TestControl { RenderAction = s => s.DrawRectangle(brush, new Rect(0, 0, 40, 40)) };
        control.Bounds = new Rect(12, 12, 40, 40);
        control.RenderSize = new Size(40, 40);
        root.Add(control);

        new PulseAnimation { Property = "Opacity", Min = 0.1, Max = 0.9, Duration = System.TimeSpan.FromSeconds(4) }.Apply(brush);
        Compositor.StartPending();
        Assert.That(Compositor.EntryFor(brush), Is.Not.Null, "a brush-opacity pulse must be composited");

        Assert.That(renderer.RenderFrame(root), Is.True);
        var early = Center(renderer);

        // Wait for the opacity to climb toward its peak - by ELAPSED, not a fixed sleep, so the test isn't hostage to how
        // long a frame takes. The loop thread is never ticked.
        var entry = Compositor.EntryFor(brush);
        while (entry.Elapsed < 1.8) Thread.Sleep(5);   // ~0.9 of a 4-second pulse to Max: near the top

        Assert.That(renderer.RenderFrame(root), Is.True);
        var high = Center(renderer);

        Assert.That(high, Is.Not.EqualTo(early),
            "a higher brush opacity must blend a redder pixel - re-baked by the render thread with the loop thread idle");
        Assert.That(brush.Opacity, Is.EqualTo(0.1),
            "and the LIVE brush opacity is untouched: paint is not mirrored, so only the compositor changed the picture");
    }

    [Test]
    public void TheRenderThreadFadesAnElementWhileTheLoopThreadDoesNothing()
    {
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.CornflowerBlue };

        var root = new TestRoot(64, 64);
        var control = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 40, 40)) };
        control.Bounds = new Rect(12, 12, 40, 40);
        control.RenderSize = new Size(40, 40);
        root.Add(control);

        control.BeginAnimation(Adamantium.UI.Controls.Base.UIComponent.OpacityProperty,
            new DoubleAnimation { From = 0.1, To = 0.9, Duration = TimeSpan.FromSeconds(4) });
        var entry = Compositor.EntryFor(control);
        Assert.That(entry?.Channel, Is.EqualTo(CompositorChannel.Opacity), "an element's fade must be composited");
        Compositor.StartPending();

        Assert.That(renderer.RenderFrame(root), Is.True);
        var early = Center(renderer);

        while (entry.Elapsed < 1.5) Thread.Sleep(5);
        Assert.That(renderer.RenderFrame(root), Is.True);
        var later = Center(renderer);
        Assert.That(later, Is.Not.EqualTo(early), "the render thread faded the element with the loop thread never ticked");

        // A loop tick writes the same value into the property and marks the element; the render thread must keep fading
        // it afterwards rather than be pinned to what that tick wrote.
        AnimationManager.Tick(0.0);
        Assert.That(renderer.RenderFrame(root), Is.True);
        var mirrored = Center(renderer);

        while (entry.Elapsed < 3.0) Thread.Sleep(5);
        Assert.That(renderer.RenderFrame(root), Is.True);
        Assert.That(Center(renderer), Is.Not.EqualTo(mirrored), "after a loop tick the render thread still drives the fade");
    }

    [Test]
    public void AChildAddedUnderAHiddenParent_IsNotDrawn()
    {
        AnimationManager.Reset();
        RenderDirty.Clear();
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        // Not black: a frame of one color is saved in a form the loader does not read.
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Navy };

        var root = new TestRoot(64, 64);
        var page = new TestControl();
        page.Bounds = new Rect(0, 0, 64, 64);
        page.RenderSize = new Size(64, 64);
        var first = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 16, 16)) };
        first.Bounds = new Rect(4, 4, 16, 16);
        first.RenderSize = new Size(16, 16);
        page.Add(first);
        root.Add(page);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        page.Visibility = Visibility.Hidden;
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        var late = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 16, 16)) };
        late.Bounds = new Rect(24, 24, 16, 16);
        late.RenderSize = new Size(16, 16);
        page.Add(late);
        Assert.That(renderer.RenderFrame(root), Is.True);

        var (before, added) = RedAt(renderer, (12, 12), (32, 32));
        Assert.That(before, Is.Zero, "the hidden page draws nothing");
        Assert.That(added, Is.Zero, "...nor a child added to it while hidden");

        RenderDirty.Clear();
        page.Visibility = Visibility.Visible;
        Assert.That(renderer.RenderFrame(root), Is.True);

        (before, added) = RedAt(renderer, (12, 12), (32, 32));
        Assert.That(before, Is.EqualTo(255), "shown again, the page draws");
        Assert.That(added, Is.EqualTo(255), "...the child added while it was hidden too");
    }

    [Test]
    public void AChildRaisedWhileItsParentIsHidden_IsOnTopOnceShown()
    {
        AnimationManager.Reset();
        RenderDirty.Clear();
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Navy };

        var root = new TestRoot(64, 64);
        var page = new TestControl();
        page.Bounds = new Rect(0, 0, 64, 64);
        page.RenderSize = new Size(64, 64);
        var red = Square(Brushes.Red, 24);
        var green = Square(Brushes.Lime, 24);
        page.Add(red);
        page.Add(green);
        root.Add(page);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        page.Visibility = Visibility.Hidden;
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        red.ZIndex = 1;
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        page.Visibility = Visibility.Visible;
        Assert.That(renderer.RenderFrame(root), Is.True);

        Assert.That(RedAt(renderer, (32, 32), (32, 32)).A, Is.EqualTo(255), "the raised child is drawn over its sibling");
    }

    [Test]
    public void AChildMovedUnderAHiddenParent_TakesItsPlaceThereOnceShown()
    {
        AnimationManager.Reset();
        RenderDirty.Clear();
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Navy };

        // In paint order: the page, a green cover, then a holder; the red square moves from the holder into the page,
        // which puts it under the cover.
        var root = new TestRoot(64, 64);
        var page = Square(null, 0, 64);
        var cover = Square(Brushes.Lime, 24);
        var holder = Square(null, 0, 64);
        var red = Square(Brushes.Red, 24);
        holder.Add(red);
        root.Add(page);
        root.Add(cover);
        root.Add(holder);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(RedAt(renderer, (32, 32), (32, 32)).A, Is.EqualTo(255), "the red square starts over the cover");

        page.Visibility = Visibility.Hidden;
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        holder.Remove(red);
        page.Add(red);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(RedAt(renderer, (32, 32), (32, 32)).A, Is.Zero, "moved into the hidden page, it is not drawn");

        page.Visibility = Visibility.Visible;
        Assert.That(renderer.RenderFrame(root), Is.True);
        Assert.That(RedAt(renderer, (32, 32), (32, 32)).A, Is.Zero, "shown, it is under the cover");
    }

    // A strip, a page and a holder with a red square in it. Each child added to the strip halves the rank gap before the
    // page (hidden, it is no anchor) and the holder, so the 32nd since the last full walk finds no room: a renumber.
    private (OffscreenTestRenderer Renderer, TestRoot Root, TestControl Strip, TestControl Page, TestControl Holder,
        TestControl Red) RenumberStand()
    {
        AnimationManager.Reset();
        RenderDirty.Clear();
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Navy };

        var root = new TestRoot(64, 64);
        var strip = Square(null, 0, 64);
        var page = Square(null, 0, 64);
        var holder = Square(null, 0, 64);
        var red = Square(Brushes.Red, 24);
        holder.Add(red);
        root.Add(strip);
        root.Add(page);
        root.Add(holder);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        page.Visibility = Visibility.Hidden;
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        return (renderer, root, strip, page, holder, red);
    }

    private static void Grow(OffscreenTestRenderer renderer, TestRoot root, TestControl strip)
    {
        strip.Add(Square(null, 0, 1));
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
    }

    [Test]
    public void MovedUnderAHiddenParentInTheFrameThatRenumbers_IsNotDrawn()
    {
        var (renderer, root, strip, page, holder, red) = RenumberStand();
        using var _ = renderer;
        for (var i = 0; i < 31; i++) Grow(renderer, root, strip);
        Assert.That(RedAt(renderer, (32, 32), (32, 32)).A, Is.EqualTo(255), "still in the holder");

        holder.Remove(red);
        page.Add(red);
        Grow(renderer, root, strip);
        Assert.That(RedAt(renderer, (32, 32), (32, 32)).A, Is.Zero, "moved under the hidden page in the renumber frame");

        Assert.That(renderer.RenderFrame(root), Is.True);
        Assert.That(RedAt(renderer, (32, 32), (32, 32)).A, Is.Zero, "...and in the frame after");
    }

    [Test]
    public void ARenumberWhileItWaitsUnderAHiddenParent_DoesNotDrawIt()
    {
        var (renderer, root, strip, page, holder, red) = RenumberStand();
        using var _ = renderer;
        holder.Remove(red);
        page.Add(red);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        for (var i = 0; i < 40; i++)
        {
            Grow(renderer, root, strip);
            Assert.That(RedAt(renderer, (32, 32), (32, 32)).A, Is.Zero, $"child {i} added to the strip");
        }
    }

    private static TestControl Square(Brush brush, int at, int size = 16)
    {
        var square = new TestControl();
        if (brush != null) square.RenderAction = s => s.DrawRectangle(brush, new Rect(0, 0, size, size));
        square.Bounds = new Rect(at, at, size, size);
        square.RenderSize = new Size(size, size);
        return square;
    }

    private static (int A, int B) RedAt(OffscreenTestRenderer renderer, (int X, int Y) a, (int X, int Y) b)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"comp_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var px = Image.Load(path).GetPixelBuffer(0, 0);
            return ((int)(px.GetPixel<uint>(a.X, a.Y) & 0xFF), (int)(px.GetPixel<uint>(b.X, b.Y) & 0xFF));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    // A loading garland: one card drawn three times, each copy a second behind the one before it, fading 0.1 -> 0.9 over
    // four seconds, endlessly.
    private (OffscreenTestRenderer Renderer, TestRoot Root, Compositor.Entry Fade) Garland(bool endless = true)
    {
        AnimationManager.Reset();   // the heartbeat and the dirty set are shared with every fixture that ran before
        RenderDirty.Clear();
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        var renderer = new OffscreenTestRenderer(_device, factory, 64, 64) { ClearColor = Colors.Black };

        var root = new TestRoot(64, 64);
        var card = new TestControl { RenderCloneStagger = TimeSpan.FromSeconds(1) };
        card.Bounds = new Rect(0, 24, 16, 16);
        card.RenderSize = new Size(16, 16);
        var fill = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 16, 16)) };
        fill.Bounds = new Rect(0, 0, 16, 16);
        fill.RenderSize = new Size(16, 16);
        card.Add(fill);
        card.RenderClones = [Matrix4x4F.Translation(0, 0, 0), Matrix4x4F.Translation(24, 0, 0), Matrix4x4F.Translation(48, 0, 0)];
        root.Add(card);

        fill.BeginAnimation(Adamantium.UI.Controls.Base.UIComponent.OpacityProperty,
            new DoubleAnimation
            {
                From = 0.1, To = 0.9, Duration = TimeSpan.FromSeconds(4), IterationCount = endless ? double.PositiveInfinity : 1
            });
        var entry = Compositor.EntryFor(fill);
        Assert.That(entry?.Channel, Is.EqualTo(CompositorChannel.Opacity));
        Compositor.StartPending();

        Assert.That(renderer.RenderFrame(root), Is.True);   // the fade's slots are made here...
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);   // ...and drawn by from here on
        RenderDirty.Clear();
        return (renderer, root, entry);
    }

    [Test]
    public void EachCopyFadesItsStaggerBehindTheOneBefore()
    {
        var (renderer, root, entry) = Garland();
        using var _ = renderer;
        while (entry.Elapsed < 2.5) Thread.Sleep(5);
        Assert.That(renderer.RenderFrame(root), Is.True);

        var (first, second, third) = Copies(renderer);
        Assert.That(first, Is.GreaterThan(second + 20), "the first copy is a second ahead of the second");
        Assert.That(second, Is.GreaterThan(third + 20), "and the second a second ahead of the third");
    }

    [Test]
    public void AGarlandThatGrows_RunsItsNewCopiesBehindLikeTheRest()
    {
        // A garland that GROWS after it settled (a list scrolled onto more pending slots), by a thousand copies: each new
        // one takes the slot of its phase and runs its stagger behind, like the ones that were there.
        AnimationManager.Reset();
        RenderDirty.Clear();
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 128, 128) { ClearColor = Colors.Black };

        var root = new TestRoot(128, 128);
        var card = new TestControl { RenderCloneStagger = TimeSpan.FromMilliseconds(2) };
        card.Bounds = new Rect(0, 0, 2, 2);
        card.RenderSize = new Size(2, 2);
        var fill = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 2, 2)) };
        fill.Bounds = new Rect(0, 0, 2, 2);
        fill.RenderSize = new Size(2, 2);
        card.Add(fill);
        root.Add(card);

        static Matrix4x4F[] Cells(int count)
        {
            var cells = new Matrix4x4F[count];
            for (var i = 0; i < count; i++)
            {
                cells[i] = Matrix4x4F.Translation(i % 64 * 2, i / 64 * 2, 0);   // copy i in the 2 px cell (i % 64, i / 64)
            }

            return cells;
        }

        void Settle()
        {
            for (var frame = 0; frame < 8; frame++)
            {
                Assert.That(renderer.RenderFrame(root), Is.True);
                RenderDirty.Clear();
                if (renderer.Cache.LastDrawPath == DrawPath.Replay)
                {
                    return;   // from here on nothing walks again by itself
                }
            }
        }

        card.RenderClones = Cells(300);
        fill.BeginAnimation(Adamantium.UI.Controls.Base.UIComponent.OpacityProperty,
            new DoubleAnimation
            {
                From = 0.1, To = 0.9, Duration = TimeSpan.FromSeconds(4), IterationCount = double.PositiveInfinity
            });
        var entry = Compositor.EntryFor(fill);
        Compositor.StartPending();
        Settle();

        card.RenderClones = Cells(1300);
        card.InvalidateRender(false);
        RenderDirty.MarkGeometry(card);
        Settle();

        while (entry.Elapsed < 2.5) Thread.Sleep(5);
        Assert.That(renderer.RenderFrame(root), Is.True);

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"comp_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var px = Image.Load(path).GetPixelBuffer(0, 0);
            static int Red(uint pixel) => (int)(pixel & 0xFF);
            var first = Red(px.GetPixel<uint>(0, 0));                     // copy 0
            var grown = Red(px.GetPixel<uint>(1000 % 64 * 2, 1000 / 64 * 2)); // copy 1000, two seconds behind
            Assert.That(first, Is.GreaterThan(grown + 20), "a copy the garland grew into runs behind like the rest");
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Test]
    public void ALoopTickDuringTheFade_LeavesTheFrameToReplay()
    {
        // The loop mirrors the value into the property every tick. While the render thread draws the fade that write must
        // not mark the element: a cloned group cannot be patched, so each mark would cost the frame a walk.
        var (renderer, root, _) = Garland();
        using var __ = renderer;

        AnimationManager.Tick(0.0);
        Assert.That(renderer.RenderFrame(root), Is.True);

        Assert.That(renderer.Cache.LastDrawPath, Is.EqualTo(DrawPath.Replay),
            "a mirrored opacity must leave the frame clean - no patch of every copy, no walk");
    }

    [Test]
    public void OnceTheGarlandStops_EveryCopyShowsTheSame()
    {
        var (renderer, root, entry) = Garland();
        using var _ = renderer;
        while (entry.Elapsed < 2.5) Thread.Sleep(5);
        Assert.That(renderer.RenderFrame(root), Is.True);
        var (early, _, late) = Copies(renderer);
        Assert.That(early, Is.GreaterThan(late + 20), "mid-pulse the copies stand apart");

        AnimationManager.Cancel(entry.Target);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);

        var (first, second, third) = Copies(renderer);
        Assert.That(second, Is.EqualTo(first).Within(3), "the copies behind must not stay where the pulse left them");
        Assert.That(third, Is.EqualTo(first).Within(3));
    }

    [Test]
    public void AFiniteFade_PlaysOnEveryCopyTogether()
    {
        // Only a cycle folds the copies into a few phases; a fade that ends would need a slot per copy, so it is not
        // staggered at all.
        var (renderer, root, entry) = Garland(endless: false);
        using var _ = renderer;
        while (entry.Elapsed < 2.5) Thread.Sleep(5);
        Assert.That(renderer.RenderFrame(root), Is.True);

        var (first, second, third) = Copies(renderer);
        Assert.That(first, Is.GreaterThan(60), "the fade is under way");
        Assert.That(second, Is.EqualTo(first).Within(3));
        Assert.That(third, Is.EqualTo(first).Within(3));
    }

    [TestCase(0)]
    [TestCase(80)]
    public void HeldSkeletonCards_PulseOnScreen(int staggerMs)
    {
        // The sandbox's Skeletons stand, end to end: a held list draws one card at every slot, the card's fill pulses,
        // and every copy on screen pulses with it.
        AnimationManager.Reset();
        RenderDirty.Clear();
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 200, 120) { ClearColor = Colors.Black };

        var root = new TestRoot(200, 120);
        var list = new Adamantium.UI.Controls.ItemsControl
        {
            ItemSkeletonTemplate = new Adamantium.UI.Core.Templates.DataTemplate(() =>
            {
                var card = new Adamantium.UI.Controls.Panels.Grid { RenderCloneStagger = TimeSpan.FromMilliseconds(staggerMs) };
                card.Children.Add(new Adamantium.UI.Controls.Decorators.Border { Background = Brushes.Red });
                return new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = card };
            }),
            ItemsSource = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Range(0, 500)),
            ItemsPanel = new Adamantium.UI.Controls.ItemsPanelTemplate(() => new Adamantium.UI.Core.Templates.TemplateResult
            {
                RootComponent = new Adamantium.UI.Controls.Panels.WrapPanel { ItemWidth = 24, ItemHeight = 24, HoldSkeletons = true }
            }),
            Template = new Adamantium.UI.Core.Templates.ControlTemplate(() =>
            {
                var presenter = new Adamantium.UI.Controls.ItemsPresenter();
                var result = new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = presenter };
                result.RegisterName("PART_ItemsPresenter", presenter);
                return result;
            })
        };
        root.Add(list);
        list.Measure(new Size(200, 120));
        list.Arrange(new Rect(0, 0, 200, 120));
        LayoutManager.GetOrCreate(list).ExecuteLayoutPass();

        var card = System.Linq.Enumerable.Single(
            System.Linq.Enumerable.OfType<Adamantium.UI.Controls.Panels.Grid>(((IUIComponent)list.ItemsHostPanel).VisualChildren));
        var fill = card.Children[0];
        new PulseAnimation { Property = "Opacity", Min = 0.1, Max = 0.9, Duration = TimeSpan.FromSeconds(1) }
            .Apply((AdamantiumComponent)fill);
        var entry = Compositor.EntryFor((AdamantiumComponent)fill, CompositorChannel.Opacity);
        Assert.That(entry, Is.Not.Null, "the fill's pulse is composited");
        Compositor.StartPending();

        int RedOfTheSixthCard()
        {
            Assert.That(renderer.RenderFrame(root), Is.True);
            RenderDirty.Clear();
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"comp_{Guid.NewGuid():N}.png");
            renderer.Save(path, ImageFileType.Png);
            try
            {
                return (int)(Image.Load(path).GetPixelBuffer(0, 0).GetPixel<uint>(5 * 24 + 12, 12) & 0xFF);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        RedOfTheSixthCard();
        var low = int.MaxValue;
        var high = int.MinValue;
        var until = entry.Elapsed + 2.2;
        while (entry.Elapsed < until)
        {
            var red = RedOfTheSixthCard();
            low = Math.Min(low, red);
            high = Math.Max(high, red);
            Thread.Sleep(100);
        }

        Assert.That(high - low, Is.GreaterThan(100), $"over a whole cycle the copy pulses on screen (low {low}, high {high})");
    }

    private static (int First, int Second, int Third) Copies(OffscreenTestRenderer renderer)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"comp_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var px = Image.Load(path).GetPixelBuffer(0, 0);
            static int Red(uint pixel) => (int)(pixel & 0xFF);
            return (Red(px.GetPixel<uint>(8, 32)), Red(px.GetPixel<uint>(32, 32)), Red(px.GetPixel<uint>(56, 32)));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static uint Center(OffscreenTestRenderer renderer)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"comp_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try { return Image.Load(path).GetPixelBuffer(0, 0).GetPixel<uint>(32, 32); }
        finally { System.IO.File.Delete(path); }
    }

    private static (uint LeftOfCenter, uint AboveCenter) Probe(OffscreenTestRenderer renderer, out uint background)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"comp_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var px = Image.Load(path).GetPixelBuffer(0, 0);
            background = px.GetPixel<uint>(2, 2);
            return (px.GetPixel<uint>(22, 32), px.GetPixel<uint>(32, 22));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    // Two probe points: inside the square's starting place, and where it should be once it has slid.
    private static (uint Left, uint Right) Sample(OffscreenTestRenderer renderer, out uint background)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"comp_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var px = Image.Load(path).GetPixelBuffer(0, 0);
            background = px.GetPixel<uint>(2, 2);
            return (px.GetPixel<uint>(8, 32), px.GetPixel<uint>(20, 32));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    // The unit factory needs one, but nothing in this test draws a texture or text.
    private sealed class StubResourceFactory : IResourceFactory
    {
        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, System.Collections.Generic.IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => throw new NotSupportedException();
    }
}
