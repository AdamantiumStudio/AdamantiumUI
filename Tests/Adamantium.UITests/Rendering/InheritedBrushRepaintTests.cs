using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// Recoloring a brush repaints everything drawn with it, including text that inherits it (a variant switch); asserted in
// rendered pixels.
[TestFixture]
[Category("Gpu")]
public class InheritedBrushRepaintTests
{
    private const int Dim = 128;

    private sealed class FontResourceFactory : IResourceFactory
    {
        private readonly Dictionary<IGraphicsDevice, FontRenderer> _renderers = new();

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();

        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice)
        {
            if (!_renderers.TryGetValue(graphicsDevice, out var renderer))
            {
                _renderers[graphicsDevice] = renderer = new FontRenderer(graphicsDevice);
            }

            return renderer;
        }
    }

    private sealed class Scene : IDisposable
    {
        public OffscreenTestRenderer Renderer;
        public VisualRoot Root;
        public StackPanel Host;
        public TextBlock Text;

        public void Draw()
        {
            ((IMeasurableComponent)Root).Measure(new Size(Dim, Dim));
            ((IMeasurableComponent)Root).Arrange(new Rect(0, 0, Dim, Dim));
            Assert.That(Renderer.RenderFrame(Root), Is.True, "off-screen frame must render");
            RenderDirty.Clear();   // no UIApplication here: the harness clears what the app clears per frame
        }

        public void Dispose() => Renderer.Dispose();
    }

    // The host holds the brush; the text does NOT - it inherits Foreground, exactly as a window seeds it for a whole
    // window's worth of text.
    private static Scene NewInheritedScene(Brush ink)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new FontResourceFactory());
        var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var text = new TextBlock { Text = "Hello", FontSize = 24 };
        var host = new StackPanel { Orientation = Orientation.Vertical, Foreground = ink };
        host.Children.Add(text);

        var scene = new Scene
        {
            Renderer = renderer,
            Root = new VisualRoot(host, Dim, Dim),
            Host = host,
            Text = text,
        };

        scene.Draw();
        return scene;
    }

    // The same scene with the brush further UP. The application never holds a text color one level above the text: the
    // window sets Foreground once and the block that draws with it is a dozen panels, presenters and templates below.
    // If the link that tells a descendant is only ever established for a DIRECT child, depth is what exposes it.
    private static Scene NewDeeplyInheritedScene(Brush ink, int depth)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new FontResourceFactory());
        var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var text = new TextBlock { Text = "Hello", FontSize = 24 };
        var root = new StackPanel { Orientation = Orientation.Vertical, Foreground = ink };

        var parent = root;
        for (var i = 1; i < depth; i++)
        {
            var level = new StackPanel { Orientation = Orientation.Vertical };
            parent.Children.Add(level);
            parent = level;
        }
        parent.Children.Add(text);

        var scene = new Scene
        {
            Renderer = renderer,
            Root = new VisualRoot(root, Dim, Dim),
            Host = root,
            Text = text,
        };

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

    [Test]
    public void TheTextInheritsTheBrush_Precondition()
    {
        var ink = new SolidColorBrush(Colors.White);
        using var scene = NewInheritedScene(ink);

        Assert.That(scene.Text.Foreground, Is.SameAs(ink),
            "the whole point of the scene: the text draws with a brush it does not own");
    }

    [Test]
    public void RecoloringTheBrush_RepaintsTheInheritedText()
    {
        var ink = new SolidColorBrush(Colors.White);
        using var scene = NewInheritedScene(ink);
        var before = Pixels(scene.Renderer);

        // The precondition the first version of this test never checked: the text has to be ON SCREEN. A frame that is
        // still the clear color compares equal to itself after any change, so an empty scene reports "the recolor
        // never arrived" just as loudly as a broken one - and says nothing.
        var painted = 0;
        for (var i = 0; i < before.Length; i += 4)
            if (before[i] != 0 || before[i + 1] != 0 || before[i + 2] != 0) painted++;
        Assume.That(painted, Is.GreaterThan(0), "precondition: the text has to be drawn before it can be recolored");

        // Exactly what a variant switch does: the palette keeps the brush and writes a new color into it.
        ink.Color = Colors.Red;
        scene.Draw();

        Assert.That(DifferingPixels(before, Pixels(scene.Renderer)), Is.Not.Zero,
            "the text has to follow a brush it inherits, with nothing else nudging the frame");
    }

    // Every element painting with a brush is in its owner map, including values inherited without a Changed notification.
    [TestCase(1)]
    [TestCase(4)]
    public void TextThatInheritsABrush_IsRegisteredAsItsOwner(int depth)
    {
        var ink = new SolidColorBrush(Colors.White);
        using var scene = depth == 1 ? NewInheritedScene(ink) : NewDeeplyInheritedScene(ink, depth);

        Assume.That(scene.Text.Foreground, Is.SameAs(ink), "precondition: the block really does inherit the brush");

        Assert.That(ink.IsOwnedBy(scene.Text), Is.True,
            "the block draws with this brush, so the brush has to be able to tell it that it changed");
    }

    [TestCase(2)]
    [TestCase(4)]
    [TestCase(8)]
    public void RecoloringTheBrush_RepaintsTextInheritingFromDepth(int depth)
    {
        var ink = new SolidColorBrush(Colors.White);
        using var scene = NewDeeplyInheritedScene(ink, depth);
        var before = Pixels(scene.Renderer);

        var painted = 0;
        for (var i = 0; i < before.Length; i += 4)
            if (before[i] != 0 || before[i + 1] != 0 || before[i + 2] != 0) painted++;
        Assume.That(painted, Is.GreaterThan(0), "precondition: the text has to be drawn before it can be recolored");
        Assume.That(scene.Text.Foreground, Is.SameAs(ink), "precondition: the block really does inherit the brush");

        ink.Color = Colors.Red;
        scene.Draw();

        Assert.That(DifferingPixels(before, Pixels(scene.Renderer)), Is.Not.Zero,
            $"text {depth} levels below the element that holds the brush has to follow it too");
    }

    // The recolor survives a frame that walks instead of patching (text bakes from its component there).
    [Test]
    public void RecoloringTheBrush_RepaintsTheText_EvenOnAFrameThatWALKS()
    {
        var ink = new SolidColorBrush(Colors.White);
        using var scene = NewInheritedScene(ink);
        var before = Pixels(scene.Renderer);

        var painted = 0;
        for (var i = 0; i < before.Length; i += 4)
            if (before[i] != 0 || before[i + 1] != 0 || before[i + 2] != 0) painted++;
        Assume.That(painted, Is.GreaterThan(0), "precondition: the text has to be drawn before it can be recolored");

        // A scene this small always patches successfully, which is precisely how a defect that only ever showed on
        // WALKING frames lived through a green suite. So take the patch away and make the frame walk, as it does in an
        // application where something structural happens in the same frame as the recolor.
        RenderCache.PatchDisabled = true;
        RenderCache.ReplayDisabled = true;
        try
        {
            ink.Color = Colors.Red;
            scene.Draw();
        }
        finally
        {
            RenderCache.PatchDisabled = false;
            RenderCache.ReplayDisabled = false;
        }

        Assert.That(DifferingPixels(before, Pixels(scene.Renderer)), Is.Not.Zero,
            "a recolor must reach the text on a walking frame too, not only on a patched one");
    }

    // A subtree detached during the recolor (a parked tab, a recycled row) returns in the new color.
    [Test]
    public void RecoloringTheBrush_RepaintsASubtreeThatWasAWAYForIt()
    {
        var ink = new SolidColorBrush(Colors.White);
        using var scene = NewInheritedScene(ink);
        var before = Pixels(scene.Renderer);

        var painted = 0;
        for (var i = 0; i < before.Length; i += 4)
            if (before[i] != 0 || before[i + 1] != 0 || before[i + 2] != 0) painted++;
        Assume.That(painted, Is.GreaterThan(0), "precondition: the text has to be drawn before it can be recolored");

        scene.Host.Children.Remove(scene.Text);
        scene.Draw();

        ink.Color = Colors.Red;          // recolored while the block is nowhere
        scene.Draw();

        scene.Host.Children.Add(scene.Text);
        scene.Draw();

        Assert.That(DifferingPixels(before, Pixels(scene.Renderer)), Is.Not.Zero,
            "a subtree that was away for the recolor must come back wearing the new color");
    }

    [Test]
    public void RecoloringTheBrush_RepaintsAnOWNEDBackgroundToo()
    {
        // The half that already worked, kept as the control: if this ever fails the fault is somewhere else entirely.
        var fill = new SolidColorBrush(Colors.Blue);
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new FontResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        // A panel with nothing in it drew nothing, and the first version of this test compared a blank frame against
        // itself and called the result a defect. Give it the same content the inherited scene has, so the fill is
        // actually on screen.
        var host = new StackPanel { Orientation = Orientation.Vertical, Background = fill, Width = Dim, Height = Dim };
        host.Children.Add(new TextBlock { Text = "Hello", FontSize = 24, Foreground = Brushes.White });
        var root = new VisualRoot(host, Dim, Dim);

        void Draw()
        {
            ((IMeasurableComponent)root).Measure(new Size(Dim, Dim));
            ((IMeasurableComponent)root).Arrange(new Rect(0, 0, Dim, Dim));
            Assert.That(renderer.RenderFrame(root), Is.True);
            RenderDirty.Clear();
        }

        Draw();
        var before = Pixels(renderer);

        // The precondition every one of these tests silently assumed: the scene has to DRAW the fill. A frame that is
        // still the clear color compares equal to itself after any change, and the test would report "the recolor
        // never reached the screen" for a scene that never put anything on it.
        var painted = 0;
        for (var i = 0; i < before.Length; i += 4)
            if (before[i] != 0 || before[i + 1] != 0 || before[i + 2] != 0) painted++;
        Assume.That(painted, Is.GreaterThan(0), "precondition: the fill has to be on screen before it can be recolored");

        Assume.That(fill.SubscriberCount, Is.GreaterThan(0),
            "precondition: the element that HOLDS the brush must be registered with it");

        fill.Color = Colors.Green;
        Draw();

        // How many frames it takes for the patch to REACH the screen. One is the answer a correct patch gives. If it
        // takes several, the write is landing in a ring copy that the frame being presented is not drawing from - the
        // patch would then be correct and invisible, which is exactly what "it only updates when I touch something
        // else" looks like from outside.
        var changedOnFrame = DifferingPixels(before, Pixels(renderer)) != 0 ? 1 : 0;
        for (var frame = 2; frame <= 5 && changedOnFrame == 0; frame++)
        {
            Draw();
            if (DifferingPixels(before, Pixels(renderer)) != 0) changedOnFrame = frame;
        }

        Assert.That(changedOnFrame, Is.EqualTo(1),
            $"a recolor has to reach the screen on the very next frame (it took {changedOnFrame} - 0 means never)");
    }
}
