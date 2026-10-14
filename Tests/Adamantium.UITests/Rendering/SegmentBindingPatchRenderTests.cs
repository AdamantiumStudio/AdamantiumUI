using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A recorded segment BINDS things a patch does not rewrite - a picture's texture, a pattern's pass - so a fill whose
// record is patched in place has to still want what its segment binds. A picture swapped for another of the same size
// kept drawing the old one, and once that one was freed the next replay drew a freed texture and took the process down.
[TestFixture]
[Category("Gpu")]
public class SegmentBindingPatchRenderTests
{
    private const int Dim = 64;

    private static BitmapSource Flat(byte r, byte g)
    {
        const int src = 8;
        var pixels = new byte[src * src * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        return new BitmapSource(src, src, 1, 1, SurfaceFormat.B8G8R8A8.UNorm, pixels);
    }

    private static byte[] Pixels(OffscreenTestRenderer renderer)
    {
        using var img = renderer.RenderTarget.ResolveTexture.ReadbackToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        System.Runtime.InteropServices.Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);
        return pixels;
    }

    private static (byte R, byte G) Middle(OffscreenTestRenderer renderer)
    {
        var pixels = Pixels(renderer);
        var at = (Dim / 2 * Dim + Dim / 2) * 4;
        return (pixels[at + 2], pixels[at + 1]);
    }

    private static void Draw(OffscreenTestRenderer renderer, VisualRoot root)
    {
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
    }

    private static (OffscreenTestRenderer Renderer, TestControl Card, VisualRoot Root) Stage(System.Action<IDrawingSession> draw)
    {
        var device = GpuTestDevice.Device;
        var renderer = new OffscreenTestRenderer(device,
            new RenderUnitFactory(device, new DeviceResourceFactory(device)), Dim, Dim) { ClearColor = Colors.Black };

        var stage = new TestControl { Bounds = new Rect(0, 0, Dim, Dim), RenderSize = new Size(Dim, Dim) };
        var card = new TestControl { Bounds = new Rect(0, 0, Dim, Dim), RenderSize = new Size(Dim, Dim), RenderAction = draw };
        stage.Add(card);
        var root = new VisualRoot(stage, Dim, Dim);

        Draw(renderer, root);
        Draw(renderer, root);
        return (renderer, card, root);
    }

    [Test]
    public void APictureFillSwappedForAnother_IsDrawnWithTheNewTexture()
    {
        var red = Flat(255, 0);
        var green = Flat(0, 255);
        var (renderer, card, root) = Stage(s => s.DrawRectangle(new ImageBrush(red), new Rect(0, 0, Dim, Dim)));
        using var _ = renderer;
        Assume.That(Middle(renderer).R, Is.GreaterThan(200), "the first picture is drawn");

        card.RenderAction = s => s.DrawRectangle(new ImageBrush(green), new Rect(0, 0, Dim, Dim));
        card.Invalidate();
        Draw(renderer, root);

        var drawn = Middle(renderer);
        Assert.Multiple(() =>
        {
            Assert.That(drawn.G, Is.GreaterThan(200), "the new picture is drawn");
            Assert.That(drawn.R, Is.LessThan(50), "the old one is not");
        });

        red.Dispose();
        green.Dispose();
    }

    [Test]
    public void APatternTurnedIntoAnotherKind_IsDrawnAsTheNewKind()
    {
        static PatternBrush Of(PatternType kind) =>
            new() { Pattern = kind, Color1 = Colors.Red, Color2 = Colors.Blue };

        var (renderer, card, root) = Stage(s => s.DrawRectangle(Of(PatternType.Checkerboard), new Rect(0, 0, Dim, Dim)));
        using var _ = renderer;

        card.RenderAction = s => s.DrawRectangle(Of(PatternType.Stripes), new Rect(0, 0, Dim, Dim));
        card.Invalidate();
        Draw(renderer, root);
        var patched = Pixels(renderer);

        RenderDirty.MarkStructural();
        Draw(renderer, root);
        Assume.That(renderer.Cache.LastFrameReplayed, Is.False, "the reference frame walks");
        var walked = Pixels(renderer);

        var differing = 0;
        for (var i = 0; i < patched.Length; i += 4)
        {
            if (patched[i] != walked[i] || patched[i + 1] != walked[i + 1] || patched[i + 2] != walked[i + 2])
            {
                differing++;
            }
        }

        Assert.That(differing, Is.Zero, "the patched frame draws the old kind");
    }
}
