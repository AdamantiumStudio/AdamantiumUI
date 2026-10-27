using System;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
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

// Soft edges and a reflection are an element's own, as its shadow is: drawn through a layer, the edge melting in from the
// element's outline, the mirror image laid under its bottom edge and fading away downward.
[TestFixture]
[Category("Gpu")]
public class SoftEdgeReflectionRenderTests
{
    private IGraphicsDevice _device;

    [SetUp]
    public void SetUp() => _device = GpuTestDevice.Device;

    [TearDown]
    public void TearDown() => AnimationManager.Reset();

    // A 40x40 element at (4,4) painting its top half red and its bottom half lime, over blue.
    private static (TestRoot Root, TestControl Element) Element(Action<TestControl> set)
    {
        var root = new TestRoot(64, 128);
        var element = new TestControl
        {
            RenderAction = s => s
                .DrawRectangle(Brushes.Red, new Rect(0, 0, 40, 20))
                .DrawRectangle(Brushes.Lime, new Rect(0, 20, 40, 20))
        };
        element.Bounds = new Rect(4, 4, 40, 40);
        element.RenderSize = new Size(40, 40);
        set(element);
        root.Add(element);
        return (root, element);
    }

    private uint[] Render(TestRoot root, params (int X, int Y)[] at) => Render(root, new StubResourceFactory(), at);

    private uint[] Render(TestRoot root, IResourceFactory resources, params (int X, int Y)[] at)
    {
        var factory = new RenderUnitFactory(_device, resources);
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 128) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        Assert.That(renderer.RenderFrame(root), Is.True);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"soft_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var px = Image.Load(path).GetPixelBuffer(0, 0);
            var pixels = new uint[at.Length];
            for (var i = 0; i < at.Length; i++)
            {
                pixels[i] = px.GetPixel<uint>(at[i].X, at[i].Y);
            }

            return pixels;
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    // Drawn, changed, drawn again by the same renderer: what the cache keeps across the change is what is tested.
    private uint[] RenderChanged(TestRoot root, Action change, params (int X, int Y)[] at) =>
        RenderChanged(root, new StubResourceFactory(), change, at);

    private uint[] RenderChanged(TestRoot root, IResourceFactory resources, Action change, params (int X, int Y)[] at)
    {
        var factory = new RenderUnitFactory(_device, resources);
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 128) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        change();
        Assert.That(renderer.RenderFrame(root), Is.True);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"soft_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var px = Image.Load(path).GetPixelBuffer(0, 0);
            var pixels = new uint[at.Length];
            for (var i = 0; i < at.Length; i++)
            {
                pixels[i] = px.GetPixel<uint>(at[i].X, at[i].Y);
            }

            return pixels;
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static int Red(uint pixel) => (int)(pixel & 0xFF);

    private static int Green(uint pixel) => (int)((pixel >> 8) & 0xFF);

    private static int Blue(uint pixel) => (int)((pixel >> 16) & 0xFF);

    [Test]
    public void ASoftEdge_MeltsTheElementFromItsOutlineIn()
    {
        var (root, _) = Element(e => e.SoftEdge = 10);

        var px = Render(root, (5, 19), (9, 19), (24, 19));

        Assert.Multiple(() =>
        {
            Assert.That(Red(px[0]), Is.LessThan(90), "faint a pixel in from the edge");
            Assert.That(Red(px[1]), Is.InRange(150, 225), "half way through the fade, most of it - it eases in");
            Assert.That(Red(px[2]), Is.GreaterThan(240), "past the fade, all of it");
        });
    }

    // The fade follows what the element draws, not its box: a corner the clip rounds fades along the curve.
    [Test]
    public void ASoftEdge_FollowsWhatTheElementDraws()
    {
        var (square, _) = Element(e => e.SoftEdge = 4);
        var (rounded, _) = Element(e =>
        {
            e.SoftEdge = 4;
            e.ClipToBounds = true;
            e.ClipCornerRadius = new ProceduralGeometry.CornerRadius(12);
        });

        var px = (8, 8);
        Assert.Multiple(() =>
        {
            Assert.That(Red(Render(square, px)[0]), Is.GreaterThan(220), "four pixels in from both square edges, nearly all");
            Assert.That(Red(Render(rounded, px)[0]), Is.InRange(40, 190), "a pixel and a half inside the curve, fading");
        });
    }

    [Test]
    public void ASoftEdge_FollowsAnEllipse()
    {
        var (root, _) = Element(e =>
        {
            e.SoftEdge = 6;
            e.RenderAction = s => s.DrawEllipse(new Rect(0, 0, 40, 40), Brushes.Red, 0, 360, ProceduralGeometry.Shapes.EllipseType.EdgeToEdge);
        });

        // On the diagonal, two pixels inside the circle; and its middle.
        var px = Render(root, (11, 11), (24, 24));

        Assert.Multiple(() =>
        {
            Assert.That(Red(px[0]), Is.LessThan(170), "near the curve, fading");
            Assert.That(Red(px[1]), Is.GreaterThan(240), "the middle in full");
        });
    }

    // A layer kept from the frame before is faded again when only the width changes.
    [Test]
    public void ASoftEdgeChanged_OnAKeptLayer_FadesAgain()
    {
        var (root, element) = Element(e => e.SoftEdge = 2);

        var px = RenderChanged(root, () => element.SoftEdge = 12, (6, 19), (24, 19));

        Assert.Multiple(() =>
        {
            Assert.That(Red(px[0]), Is.LessThan(120), "two pixels in, deep in the wider fade");
            Assert.That(Red(px[1]), Is.GreaterThan(240), "the middle in full");
        });
    }

    // A container that draws nothing itself keeps its layer when only its child is drawn; widening its soft edge must
    // fade that kept drawing again, over the target it was laid on.
    [Test]
    public void ASoftEdgeChanged_OnAContainerKeptWhole_FadesAgain()
    {
        var root = new TestRoot(64, 128);
        var holder = new TestControl { SoftEdge = 2 };
        holder.Bounds = new Rect(4, 4, 40, 40);
        holder.RenderSize = new Size(40, 40);
        var child = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Lime, new Rect(0, 0, 40, 40)) };
        child.Bounds = new Rect(0, 0, 40, 40);
        child.RenderSize = new Size(40, 40);
        holder.Add(child);
        root.Add(holder);

        var px = RenderChanged(root, () => holder.SoftEdge = 12, (6, 24));

        Assert.That(Green(px[0]), Is.LessThan(120), "two pixels in, deep in the wider fade");
    }

    // The window's edge is no outline: an element running past it does not fade there.
    [Test]
    public void ASoftEdge_DoesNotFadeWhereAClipCutsTheDrawing()
    {
        var (root, _) = Element(e =>
        {
            e.SoftEdge = 8;
            e.Bounds = new Rect(-30, 4, 60, 40);
            e.RenderSize = new Size(60, 40);
            e.RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 60, 40));
        });

        var px = Render(root, (1, 24), (28, 24));

        Assert.Multiple(() =>
        {
            Assert.That(Red(px[0]), Is.GreaterThan(240), "thirty pixels in, at the window's edge: in full");
            Assert.That(Red(px[1]), Is.LessThan(120), "two pixels in from its own edge: fading");
        });
    }

    // An element that clips itself fades at its own edge even where its content runs on past the window: its own clip,
    // not the window's, is what cuts it there.
    [Test]
    public void ASoftEdge_FadesAtItsOwnClip_ThoughItsContentRunsPastTheWindow()
    {
        var root = new TestRoot(64, 128);
        var holder = new TestControl { SoftEdge = 8, ClipToBounds = true };
        holder.Bounds = new Rect(20, 4, 30, 40);
        holder.RenderSize = new Size(30, 40);
        var child = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Lime, new Rect(0, 0, 100, 40)) };
        child.Bounds = new Rect(-40, 0, 100, 40);
        child.RenderSize = new Size(100, 40);
        holder.Add(child);
        root.Add(holder);

        var px = Render(root, (21, 24));

        Assert.That(Green(px[0]), Is.LessThan(120), "a pixel inside its own left edge, faded");
    }

    // A shape whose band reads a distance field baked from its mesh wears it from its very first frame, soft-edged too.
    [TestCase(false)]
    [TestCase(true)]
    public void AShapesGlow_ShowsFromItsFirstFrame_UnderASoftEdge(bool shownLater)
    {
        var root = new TestRoot(64, 128);
        var star = new Adamantium.UI.Controls.Shapes.Polygon
        {
            Points = new Adamantium.UI.Core.Collections.PointsCollection
            {
                new Vector2(20, 0), new Vector2(26, 14), new Vector2(40, 14), new Vector2(29, 23), new Vector2(33, 38),
                new Vector2(20, 29), new Vector2(7, 38), new Vector2(11, 23), new Vector2(0, 14), new Vector2(14, 14)
            },
            Fill = Brushes.Lime,
            Aura = new Aura { Radius = 8, Color = Colors.White, Opacity = 1 },
            SoftEdge = 4
        };
        root.Add(star);
        star.Measure(new Size(40, 40));
        star.Arrange(new Rect(12, 12, 40, 40));

        // Just above the top point, outside the shape: only the glow can be there.
        var resources = new DeviceResourceFactory(_device);
        uint[] px;
        if (shownLater)
        {
            star.Visibility = Visibility.Collapsed;
            px = RenderChanged(root, resources, () =>
            {
                star.Visibility = Visibility.Visible;
                star.Measure(new Size(40, 40));
                star.Arrange(new Rect(12, 12, 40, 40));
            }, (32, 10));
        }
        else
        {
            px = Render(root, resources, (32, 10));
        }

        Assert.That(Red(px[0]), Is.GreaterThan(40));
    }

    // An opacity mask reaches everything the element shows, its glow included.
    [Test]
    public void AnOpacityMask_HidesASoftEdgedElementsGlowToo()
    {
        var (root, _) = Element(e =>
        {
            e.Aura = new Aura { Radius = 10, Color = Colors.White, Opacity = 1 };
            e.SoftEdge = 6;
            e.OpacityMask = new SolidColorBrush(Colors.White) { Opacity = 0 };
        });

        Assert.That(Red(Render(root, (24, 2))[0]), Is.LessThan(10));
    }

    // Under a translucent soft-edged element its glow lies only under the fading rim, not across it.
    [Test]
    public void ASoftEdgedGlow_LiesUnderTheRimOnly()
    {
        var glow = new Aura { Radius = 10, Color = Colors.White, Opacity = 1 };
        var (soft, _) = Element(e =>
        {
            e.Aura = glow;
            e.SoftEdge = 6;
            e.Opacity = 0.3;
        });
        var (plain, _) = Element(e =>
        {
            e.Aura = glow;
            e.IsLayer = true;
            e.Opacity = 0.3;
        });

        // In the lime half, which has no red: any red there is the white glow showing through.
        var middle = (24, 30);
        Assert.Multiple(() =>
        {
            Assert.That(Red(Render(plain, middle)[0]), Is.LessThan(10), "a translucent layer with no glow beneath it");
            Assert.That(Red(Render(soft, middle)[0]), Is.LessThan(10), "nor under the soft-edged one's middle");
        });
    }

    [Test]
    public void ASoftEdgedElement_IsAsTranslucentAsItsOpacity()
    {
        var (soft, _) = Element(e =>
        {
            e.SoftEdge = 6;
            e.Opacity = 0.3;
        });

        Assert.That(Green(Render(soft, (24, 30))[0]), Is.InRange(66, 86), "lime at three tenths");
    }

    [Test]
    public void AnOpacityChange_ReachesTheGlowUnderASoftEdge()
    {
        var glow = new Aura { Radius = 10, Color = Colors.White, Opacity = 1 };
        var (changed, element) = Element(e =>
        {
            e.Aura = glow;
            e.SoftEdge = 6;
        });
        var (fresh, _) = Element(e =>
        {
            e.Aura = glow;
            e.SoftEdge = 6;
            e.Opacity = 0.3;
        });

        var outside = (24, 2);
        var afterChange = RenderChanged(changed, () => element.Opacity = 0.3, outside)[0];

        Assert.That(Red(afterChange), Is.EqualTo(Red(Render(fresh, outside)[0])).Within(4));
    }

    // A soft-edged element's own glow is part of it: its reflection mirrors that too.
    [Test]
    public void ASoftEdgedElementsReflection_MirrorsItsGlowToo()
    {
        var glow = new Aura { Radius = 10, Color = Colors.White, Opacity = 1 };
        var (plain, _) = Element(e =>
        {
            e.Aura = glow;
            e.Reflection = new Reflection { Size = 1, Opacity = 1 };
        });
        var (soft, _) = Element(e =>
        {
            e.Aura = glow;
            e.SoftEdge = 4;
            e.Reflection = new Reflection { Size = 1, Opacity = 1 };
        });

        // The glow gone by row 54, the floor there: six rows below it, the mirror image of the glow three rows under the
        // element's edge, before the mirrored element begins at 64.
        var mirroredGlow = (24, 60);
        var p = Red(Render(plain, mirroredGlow)[0]);
        Assert.Multiple(() =>
        {
            Assert.That(p, Is.GreaterThan(40));
            Assert.That(Red(Render(soft, mirroredGlow)[0]), Is.EqualTo(p).Within(8));
        });
    }

    // Only what the element draws fades: its own glow stays whole around it.
    [Test]
    public void ASoftEdge_LeavesTheElementsOwnGlowWhole()
    {
        var glow = new Aura { Radius = 10, Color = Colors.White, Opacity = 1 };
        var (plain, _) = Element(e => e.Aura = glow);
        var (soft, _) = Element(e =>
        {
            e.Aura = glow;
            e.SoftEdge = 8;
        });

        var outside = (24, 2);   // two pixels above the top edge
        Assert.That(Red(Render(soft, outside)[0]), Is.EqualTo(Red(Render(plain, outside)[0])).Within(4));
    }

    [Test]
    public void ASoftEdge_FollowsARectanglesOwnCorners()
    {
        var root = new TestRoot(64, 128);
        var rectangle = new Adamantium.UI.Controls.Shapes.Rectangle
        {
            Fill = Brushes.Red,
            CornerRadius = new ProceduralGeometry.CornerRadius(12),
            SoftEdge = 4
        };
        root.Add(rectangle);
        rectangle.Measure(new Size(40, 40));
        rectangle.Arrange(new Rect(4, 4, 40, 40));

        var px = Render(root, (8, 8), (24, 24));

        Assert.Multiple(() =>
        {
            Assert.That(Red(px[0]), Is.InRange(40, 190), "a pixel and a half inside its rounded corner, fading");
            Assert.That(Red(px[1]), Is.GreaterThan(240), "the middle in full");
        });
    }

    [Test]
    public void AReflection_MirrorsTheElementUnderItsBottomEdge_AndFadesAway()
    {
        var (root, _) = Element(e => e.Reflection = new Reflection { Size = 1, Opacity = 1 });

        // The bottom edge is row 44: the lime half is mirrored first, then the red one.
        var px = Render(root, (24, 46), (24, 70), (24, 86), (24, 100));

        Assert.Multiple(() =>
        {
            Assert.That(Green(px[0]), Is.GreaterThan(200), "right under the edge, the lime, nearly as strong");
            Assert.That(Red(px[1]), Is.InRange(60, 200), "further down, the red, fading");
            Assert.That(Red(px[1]), Is.LessThan(Red(Render(Element(e => { }).Root, (24, 10))[0])), "weaker than itself");
            Assert.That(Red(px[2]) + Green(px[2]), Is.LessThan(20), "past its length, gone");
            Assert.That(Blue(px[3]), Is.GreaterThan(240), "and the background below untouched");
        });
    }

    // The floor lies under all the element draws, its glow too: the reflection turns the whole of it over, its glow
    // first, and leaves the element's own glow whole above.
    [Test]
    public void AReflection_TurnsOverTheGlowUnderIt()
    {
        var glow = new Aura { Radius = 10, Color = Colors.White, Opacity = 1 };
        var (plain, _) = Element(e => e.Aura = glow);
        var (mirrored, _) = Element(e =>
        {
            e.Aura = glow;
            e.Reflection = new Reflection { Size = 1, Opacity = 1, OffsetY = 8 };
        });

        // Edge at row 44, the glow gone by 54, floor at 58, the mirrored glow from 62, the mirrored element from 72.
        (int, int)[] rows = [(24, 47), (24, 58), (24, 66), (24, 76)];
        var m = Render(mirrored, rows);
        var p = Render(plain, rows);

        Assert.Multiple(() =>
        {
            Assert.That(Red(m[0]), Is.EqualTo(Red(p[0])).Within(3), "above the floor, only the element itself");
            Assert.That(Red(p[0]), Is.GreaterThan(40), "its glow there");
            Assert.That(Red(m[1]), Is.EqualTo(Red(p[1])).Within(3), "between the glow and its mirror image, nothing");
            Assert.That(Red(m[2]), Is.GreaterThan(Red(p[2]) + 20), "the glow's mirror image");
            Assert.That(Green(m[3]), Is.GreaterThan(150), "then the element's lime half");
        });
    }

    // A shadow thrown down reaches past the glow: the floor goes under it, so the mirrored shadow keeps its round
    // corners instead of starting at the floor as a straight cut.
    [Test]
    public void AReflection_TurnsOverTheShadowWhole()
    {
        var (root, _) = Element(e =>
        {
            e.Shadow = new Shadow { OffsetX = 0, OffsetY = 10, BlurRadius = 6, Color = Colors.White, Opacity = 1 };
            e.Reflection = new Reflection { Size = 1, Opacity = 1, Fade = 0 };
        });

        // The shadow's bottom edge at row 54 and its blur gone by 60: the floor there, and its mirror image fading in
        // from there down as it fades out above.
        var px = Render(root, (24, 56), (24, 63), (24, 58), (24, 61), (24, 70));

        Assert.Multiple(() =>
        {
            Assert.That(Red(px[0]), Is.InRange(40, 200), "the shadow's blur, half way out");
            Assert.That(Red(px[1]), Is.EqualTo(Red(px[0])).Within(8), "the mirror image, as strong as what it mirrors");
            Assert.That(Red(px[3]), Is.EqualTo(Red(px[2])).Within(8));
            Assert.That(Red(px[4]), Is.GreaterThan(240), "then the mirrored shadow itself");
        });
    }

    // Right under the edge, beside the element, its glow runs on into the reflection's: a row below the floor no brighter
    // than the row above it, however strong the reflection.
    [Test]
    public void AReflection_MeetsTheGlowWithoutASeam()
    {
        var (root, _) = Element(e =>
        {
            e.Aura = new Aura { Radius = 10, Color = Colors.White, Opacity = 1 };
            e.Reflection = new Reflection { Size = 1, Opacity = 0.5 };
        });

        var px = Render(root, (1, 43), (1, 44));

        Assert.That(Red(px[1]), Is.EqualTo(Red(px[0])).Within(12));
    }

    // Raised less than nothing, the floor lies inside the element: what it draws solid stays in front of it.
    [Test]
    public void ANegativeOffsetY_KeepsTheElementWhole()
    {
        var (root, _) = Element(e => e.Reflection = new Reflection { Size = 1, Opacity = 1, OffsetY = -8, OffsetX = 20 });

        var px = Render(root, (24, 42));

        Assert.That(Green(px[0]), Is.GreaterThan(240), "the element's own lime, two rows above its edge");
    }

    [Test]
    public void AReflectionsOffsetX_MovesItSideways()
    {
        var (root, _) = Element(e => e.Reflection = new Reflection { Size = 1, Opacity = 1, OffsetX = 10 });

        var px = Render(root, (50, 54), (6, 54));

        Assert.Multiple(() =>
        {
            Assert.That(Green(px[0]), Is.GreaterThan(150), "the lime half, moved past the element's right edge");
            Assert.That(Blue(px[1]), Is.GreaterThan(240), "and none of it where it moved away from");
        });
    }

    [Test]
    public void AReflectionsFade_DecidesHowMuchOfItMelts()
    {
        var (sharp, _) = Element(e => e.Reflection = new Reflection { Size = 1, Opacity = 1, Fade = 0 });
        var (melting, _) = Element(e => e.Reflection = new Reflection { Size = 1, Opacity = 1, Fade = 1 });

        // Thirty rows under the edge, three quarters of the way down the mirrored element; and past its end.
        var px = Render(sharp, (24, 74), (24, 88));

        Assert.Multiple(() =>
        {
            Assert.That(Red(px[0]), Is.GreaterThan(230), "no fade: shown at full strength");
            Assert.That(Red(Render(melting, (24, 74))[0]), Is.LessThan(120), "faded all the way: mostly gone there");
            Assert.That(Red(px[1]), Is.LessThan(10), "past its end, nothing either way");
        });
    }

    [Test]
    public void AReflectionsOffsetY_LeavesAGapUnderTheEdge()
    {
        var (root, _) = Element(e => e.Reflection = new Reflection { Size = 1, Opacity = 1, OffsetY = 8 });

        var px = Render(root, (24, 47), (24, 54));

        Assert.Multiple(() =>
        {
            Assert.That(Blue(px[0]), Is.GreaterThan(240), "in the gap, the background");
            Assert.That(Green(px[1]), Is.GreaterThan(150), "the reflection starts below it");
        });
    }

    [Test]
    public void AReflectionSwitchedOff_DrawsNothingUnderTheElement()
    {
        var (root, _) = Element(e => e.Reflection = new Reflection { Size = 1, Opacity = 1, IsEnabled = false });

        var px = Render(root, (24, 46), (24, 24));

        Assert.Multiple(() =>
        {
            Assert.That(Blue(px[0]), Is.GreaterThan(240), "no reflection");
            Assert.That(Green(px[1]), Is.GreaterThan(240), "the element itself as it was");
        });
    }

    [Test]
    public void AReflectionChangedInPlace_ShowsOnTheNextFrame()
    {
        var reflection = new Reflection { Size = 1, Opacity = 1 };
        var (root, _) = Element(e => e.Reflection = reflection);
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 128) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        reflection.OffsetY = 20;
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"soft_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var px = Image.Load(path).GetPixelBuffer(0, 0).GetPixel<uint>(24, 50);
            Assert.That(Blue(px), Is.GreaterThan(240), "the gap opened");
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Test]
    public void AnElementThatClipsToItsBounds_StillShowsItsReflection()
    {
        var (root, _) = Element(e =>
        {
            e.ClipToBounds = true;
            e.Reflection = new Reflection { Size = 1, Opacity = 1 };
        });

        var px = Render(root, (24, 46));

        Assert.That(Green(px[0]), Is.GreaterThan(200), "its own clip does not cut what hangs below it");
    }

    [Test]
    public void AReflectionInsideAnotherLayer_IsNotCutAtItsEdge()
    {
        var root = new TestRoot(64, 128);
        var outer = new TestControl { IsLayer = true };
        outer.Bounds = new Rect(0, 0, 64, 50);
        outer.RenderSize = new Size(64, 50);
        var element = new TestControl
        {
            Reflection = new Reflection { Size = 1, Opacity = 1 },
            RenderAction = s => s.DrawRectangle(Brushes.Lime, new Rect(0, 0, 40, 40))
        };
        element.Bounds = new Rect(4, 4, 40, 40);
        element.RenderSize = new Size(40, 40);
        outer.Add(element);
        root.Add(outer);

        var px = Render(root, (24, 56));

        Assert.That(Green(px[0]), Is.GreaterThan(120), "twelve pixels down, still there");
    }

    [Test]
    public void AReflectionSwitchedOff_CostsNoLayer()
    {
        var (root, _) = Element(e => e.Reflection = new Reflection { IsEnabled = false });
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 128) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);

        Assert.That(renderer.Cache.LayersRedrawn + renderer.Cache.LayersReused, Is.Zero);
    }

    [Test]
    public void ASoftEdgeTakenAway_GivesUpTheBandsTexturesOfALayerThatStays()
    {
        var (root, element) = Element(e =>
        {
            e.Aura = new Aura { Radius = 8, Color = Colors.White, Opacity = 1 };
            e.Reflection = new Reflection { Size = 1, Opacity = 1 };
            e.SoftEdge = 6;
        });
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 128) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        Assert.That(renderer.Cache.LayersHoldingBands, Is.EqualTo(1));

        element.SoftEdge = 0;
        Assert.That(renderer.RenderFrame(root), Is.True);

        Assert.That(renderer.Cache.LayersRedrawn + renderer.Cache.LayersReused, Is.EqualTo(1), "the reflection keeps the layer");
        Assert.That(renderer.Cache.LayersHoldingBands, Is.Zero);
    }

    // A whole reflection is all the element draws: the glow above its top edge too, not cut where the element ends.
    [Test]
    public void AWholeReflection_KeepsTheGlowAboveTheElement()
    {
        var (root, _) = Element(e =>
        {
            e.Aura = new Aura { Radius = 10, Color = Colors.White, Opacity = 1 };
            e.Reflection = new Reflection { Size = 1, Opacity = 1, Fade = 0 };
        });

        // The glow gone by row 54, the floor there: row 1, over the top edge, mirrored at row 106.
        var px = Render(root, (24, 1), (24, 106));

        Assert.Multiple(() =>
        {
            Assert.That(Red(px[0]), Is.GreaterThan(40), "the glow over the element's top edge");
            Assert.That(Red(px[1]), Is.EqualTo(Red(px[0])).Within(12), "and its mirror image, whole");
        });
    }

    // What the element's own clip cuts away is not drawn, so the floor is not under it.
    [Test]
    public void AReflection_StandsUnderWhatTheClipLeaves()
    {
        var root = new TestRoot(64, 128);
        var element = new TestControl { ClipToBounds = true, Reflection = new Reflection { Size = 1, Opacity = 1 } };
        element.Bounds = new Rect(4, 4, 40, 40);
        element.RenderSize = new Size(40, 40);
        var tall = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Lime, new Rect(0, 0, 40, 400)) };
        tall.Bounds = new Rect(0, 0, 40, 400);
        tall.RenderSize = new Size(40, 400);
        element.Add(tall);
        root.Add(element);

        var px = Render(root, (24, 50));

        Assert.That(Green(px[0]), Is.GreaterThan(200), "the reflection right under the element's edge");
    }

    // Squeezed across, the shadow is drawn at the lesser scale: the floor lies where it ends, not further down.
    [Test]
    public void AReflection_UnderASqueezedElement_MeetsItsShadow()
    {
        var (root, _) = Element(e =>
        {
            e.RenderTransform = new Transform { ScaleX = 0.5, ScaleY = 1 };
            e.Shadow = new Shadow { OffsetY = 10, BlurRadius = 2, Color = Colors.White, Opacity = 1 };
            e.Reflection = new Reflection { Size = 1, Opacity = 1 };
        });

        // The shadow, five pixels down at half scale, ends by row 50: the floor there, row 47 mirrored at row 52.
        var px = Render(root, (10, 47), (10, 52));

        Assert.Multiple(() =>
        {
            Assert.That(Red(px[0]), Is.GreaterThan(200), "the shadow under the element");
            Assert.That(Red(px[1]), Is.GreaterThan(200), "its mirror image, right under the floor");
        });
    }

    // A full walk re-records only what changed: a layer that did not change keeps what it draws. It used to lose it, and
    // with nothing else changing no frame came to bring it back - the element was simply gone.
    [TestCase("reflection")]
    [TestCase("soft edge")]
    [TestCase("layer")]
    public void AFullWalk_LeavesAnUnchangedLayerDrawn(string kind)
    {
        var (root, _) = Element(e =>
        {
            e.Reflection = kind == "reflection" ? new Reflection { Size = 1, Opacity = 1 } : null;
            e.SoftEdge = kind == "soft edge" ? 4 : 0;
            e.IsLayer = kind == "layer";
        });

        var px = RenderChanged(root, () => RenderDirty.MarkStructural(), (24, 30));

        Assert.That(Green(px[0]), Is.GreaterThan(240), "the lime half, still there");
    }

    [Test]
    public void ASoftEdgeOnAnElementOfNoSize_LeavesWhatItsChildrenDraw()
    {
        var root = new TestRoot(64, 128);
        var holder = new TestControl { SoftEdge = 6 };
        holder.Bounds = new Rect(4, 4, 0, 0);
        holder.RenderSize = new Size(0, 0);
        var bar = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 40, 20)) };
        bar.Bounds = new Rect(0, 0, 40, 20);
        bar.RenderSize = new Size(40, 20);
        holder.Add(bar);
        root.Add(holder);

        var px = Render(root, (24, 14));

        Assert.That(Red(px[0]), Is.GreaterThan(240));
    }

    [Test]
    public void WithNeither_TheElementIsNoLayer()
    {
        var (root, _) = Element(e => { });
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 128) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);

        Assert.That(renderer.Cache.LayersRedrawn + renderer.Cache.LayersReused, Is.Zero);
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
