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
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// Borders through each slot-rewriting path (paint change, neighbor unit count change, resize): the patched frame must
// equal a full walk.
[TestFixture]
[Category("Gpu")]
public class BorderPatchRenderTests
{
    private const int Dim = 96;
    private const int Rows = 4;
    private const int RowHeight = 20;

    private sealed class Scene : IDisposable
    {
        public OffscreenTestRenderer Renderer;
        public VisualRoot Root;
        public TestControl[] Extras;      // draw nothing until asked - the unit-count change next door
        public TestControl[] Borders;     // one bordered card per row

        public void Draw()
        {
            Assert.That(Renderer.RenderFrame(Root), Is.True, "off-screen frame must render");
            RenderDirty.Clear();
        }

        public void Dispose() => Renderer.Dispose();
    }

    private static TestControl Placed(Rect bounds) =>
        new() { Bounds = bounds, RenderSize = new Size(bounds.Width, bounds.Height) };

    // Rows of bordered cards, each with an empty sibling in front of it. Unequal sides and unequal corners on purpose:
    // that is the case that used to leave the batch entirely, so it is the one whose retained slot is newest.
    private static Scene NewScene()
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var borders = new TestControl[Rows];
        var extras = new TestControl[Rows];
        for (var i = 0; i < Rows; i++)
        {
            var y = i * RowHeight;
            borders[i] = Placed(new Rect(0, y, Dim, RowHeight));
            borders[i].RenderAction = s => s.DrawBorder(Brushes.Blue, new Rect(4, 2, Dim - 8, RowHeight - 4),
                new CornerRadius(6, 0, 6, 0), Brushes.Red, new Thickness(2, 5, 2, 5));
            stage.Add(borders[i]);

            extras[i] = Placed(new Rect(0, y, Dim, RowHeight));
            stage.Add(extras[i]);
        }

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Extras = extras, Borders = borders };
        scene.Draw();
        return scene;
    }

    private static byte[] Pixels(OffscreenTestRenderer renderer)
    {
        using var img = renderer.RenderTarget.ResolveTexture.ReadbackToImage();
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
        var walked = Pixels(scene.Renderer);
        Assert.That(DifferingPixels(patched, walked), Is.Zero, because + " " + Where(patched, walked));
    }

    // Names the region that differs, and one pixel out of it. A count alone cannot tell "the newcomer landed wrong" from
    // "a card lost its ring" - and those are opposite bugs.
    private static string Where(byte[] a, byte[] b)
    {
        int minX = Dim, minY = Dim, maxX = -1, maxY = -1, sample = -1;
        for (var y = 0; y < Dim; y++)
        {
            for (var x = 0; x < Dim; x++)
            {
                var i = (y * Dim + x) * 4;
                if (a[i] == b[i] && a[i + 1] == b[i + 1] && a[i + 2] == b[i + 2]) continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                if (sample < 0) sample = i;
            }
        }

        if (sample < 0) return "(identical)";
        return $"differs in x {minX}..{maxX}, y {minY}..{maxY}; patched rgb=({a[sample + 2]},{a[sample + 1]},{a[sample]}) walked rgb=({b[sample + 2]},{b[sample + 1]},{b[sample]})";
    }

    private static int BorderPixels(byte[] px)
    {
        var count = 0;
        for (var i = 0; i < px.Length; i += 4)
        {
            if (px[i + 2] > 128 && px[i] < 96) count++;   // BGRA: red ring, blue fill
        }

        return count;
    }

    // A paint-only change on the bordered card itself: its slot is rewritten in place. The ring has to survive being
    // re-baked through the patch path, not just through a walk.
    [Test]
    public void RecoloringABorderedCard_KeepsItsRing()
    {
        using var scene = NewScene();
        var ringBefore = BorderPixels(Pixels(scene.Renderer));
        Assert.That(ringBefore, Is.GreaterThan(0), "the setup must actually draw rings");

        scene.Borders[1].RenderAction = s => s.DrawBorder(Brushes.Blue, new Rect(4, 2, Dim - 8, RowHeight - 4),
            new CornerRadius(6, 0, 6, 0), Brushes.Red, new Thickness(2, 5, 2, 5));
        scene.Borders[1].Invalidate();
        scene.Draw();

        var patched = Pixels(scene.Renderer);
        Assert.That(BorderPixels(patched), Is.EqualTo(ringBefore),
            "a re-baked bordered card must still ring - a lost Inset draws the fill and nothing else");
        AssertMatchesAFullWalk(scene, patched, "the patched frame must be pixel-identical to a full walk");
    }

    // A neighbor's unit count going 0 -> 1 takes the splice path; a newcomer ranked inside a segment's span needs the
    // segment cut at its rank.
    [Test]
    public void ANeighborAppearing_LandsOnTopOfTheCard_NotUnderIt()
    {
        using var scene = NewScene();
        Assert.That(BorderPixels(Pixels(scene.Renderer)), Is.GreaterThan(0), "the setup must actually draw rings");

        scene.Extras[2].RenderAction = s => s.DrawRectangle(Brushes.Green, new Rect(0, 0, 8, RowHeight));
        scene.Extras[2].Invalidate();
        scene.Draw();

        var patched = Pixels(scene.Renderer);
        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True,
            "and it must still be a patch - a correct picture bought with a full walk is the other half of this bug");
        // The newcomer paints AFTER its row's card, so it legitimately covers part of that card's ring. What it may not do
        // is differ from what the walk draws, which is what the count alone cannot tell.
        AssertMatchesAFullWalk(scene, patched, "the newcomer must land exactly where a full walk puts it");
    }

    // Two patches in one frame around a split: patches resolved before the split still name their segment (stable ids).
    [Test]
    public void TwoPatchesInOneFrame_AroundASplit_EachReissuesItsOwnLayer()
    {
        using var scene = NewScene();
        Assert.That(BorderPixels(Pixels(scene.Renderer)), Is.GreaterThan(0), "the setup must actually draw rings");

        // The newcomer: rank inside a recorded segment's span, so placing it cuts that segment.
        scene.Extras[1].RenderAction = s => s.DrawRectangle(Brushes.Green, new Rect(0, 0, 8, RowHeight));
        scene.Extras[1].Invalidate();

        // ...and a LATER card whose drawn unit count changes in the same frame, so it goes through the layer re-issue.
        scene.Borders[3].RenderAction = s =>
        {
            s.DrawRectangle(Brushes.Blue, new Rect(4, 2, Dim - 8, RowHeight - 4));
            s.DrawRectangle(Brushes.Yellow, new Rect(6, 4, 10, RowHeight - 8));
        };
        scene.Borders[3].Invalidate();

        scene.Draw();

        var patched = Pixels(scene.Renderer);
        AssertMatchesAFullWalk(scene, patched,
            "each patch must land in its OWN layer, however the split moved the segments around it");
    }

    // ...and again, with the newcomer VANISHING - the other half of the splice, where a run is excised and what follows
    // is renumbered down. Repeated on purpose: the arena reuses freed blocks, so the second lap hands out a USED slot.
    [Test]
    public void ANeighborComingAndGoing_LeavesEveryRingIntact()
    {
        using var scene = NewScene();
        var ringBefore = BorderPixels(Pixels(scene.Renderer));

        for (var lap = 0; lap < 3; lap++)
        {
            scene.Extras[1].RenderAction = s => s.DrawRectangle(Brushes.Green, new Rect(0, 0, 8, RowHeight));
            scene.Extras[1].Invalidate();
            scene.Draw();

            scene.Extras[1].RenderAction = null;
            scene.Extras[1].Invalidate();
            scene.Draw();

            Assert.That(BorderPixels(Pixels(scene.Renderer)), Is.EqualTo(ringBefore), $"after lap {lap}");
        }

        AssertMatchesAFullWalk(scene, Pixels(scene.Renderer), "three laps of appear/vanish must leave the rings where they were");
    }

    // A RESIZE re-lays out every row and re-bakes their instances - the path that carried today's other report ("dragged a
    // slider and it went"). A border must come out of it with the same ring it went in with.
    [Test]
    public void ResizingTheViewport_KeepsEveryRing()
    {
        using var scene = NewScene();
        var ringBefore = BorderPixels(Pixels(scene.Renderer));

        for (var i = 0; i < Rows; i++)
        {
            var y = i * RowHeight;
            var width = Dim - 8 - i;   // every card a different width, as a size drag gives them
            scene.Borders[i].RenderAction = s => s.DrawBorder(Brushes.Blue, new Rect(4, 2, width, RowHeight - 4),
                new CornerRadius(6, 0, 6, 0), Brushes.Red, new Thickness(2, 5, 2, 5));
            scene.Borders[i].Invalidate();
        }

        scene.Draw();
        var patched = Pixels(scene.Renderer);

        Assert.That(BorderPixels(patched), Is.GreaterThan(ringBefore - Rows * RowHeight),
            "narrowing the cards may shorten the rings a little; losing one outright is the failure this watches for");
        AssertMatchesAFullWalk(scene, patched, "a re-sized bordered card must match what a walk draws");
    }

    // The unit factory needs one, but nothing here draws a texture or text.
    private sealed class StubResourceFactory : IResourceFactory
    {
        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => throw new NotSupportedException();
    }
}
