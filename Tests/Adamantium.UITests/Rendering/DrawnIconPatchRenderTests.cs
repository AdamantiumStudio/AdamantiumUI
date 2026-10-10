using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

[TestFixture]
[Category("Gpu")]
public class DrawnIconPatchRenderTests
{
    private const int Dim = 128;

    private sealed class Icons : MeasurableUIComponent
    {
        private readonly DrawingImage _icon;

        public Icons(DrawingImage icon, int count)
        {
            _icon = icon;
            Count = count;
        }

        public int Count { get; set; }

        protected override Size MeasureOverride(Size availableSize) => new(32, Dim);

        protected override void OnRender(IDrawingContext context)
        {
            var session = context.ForControl(this);
            for (var i = 0; i < Count; i++)
            {
                _icon.Render(session, new Rect(10, 40 + i * 24, 12, 20));
            }
        }
    }

    private sealed class Shapes : MeasurableUIComponent
    {
        private readonly Geometry _shape = new RectangleGeometry { Rect = new Rect(0, 0, 12, 20) };
        private readonly Brush _fill = new SolidColorBrush(Colors.White);

        public int Count { get; set; } = 1;

        public Pen Outline { get; set; }

        protected override Size MeasureOverride(Size availableSize) => new(32, Dim);

        protected override void OnRender(IDrawingContext context)
        {
            var session = context.ForControl(this);
            for (var i = 0; i < Count; i++)
            {
                session.DrawGeometry(_fill, _shape, Outline, Matrix4x4F.Translation(10, 40 + i * 24, 0));
            }
        }
    }

    private sealed class Scene : IDisposable
    {
        public OffscreenTestRenderer Renderer;
        public VisualRoot Root;
        public Icons Toolbar;
        public Icons Margin;
        public SolidColorBrush MarginBrush;

        public void Draw()
        {
            ((IMeasurableComponent)Root).Measure(new Size(Dim, Dim));
            ((IMeasurableComponent)Root).Arrange(new Rect(0, 0, Dim, Dim));
            Assert.That(Renderer.RenderFrame(Root), Is.True, "off-screen frame must render");
            RenderDirty.Clear();
        }

        public void Grow(Icons icons)
        {
            icons.Count++;
            icons.InvalidateRender(false);
            Draw();
        }

        public void Dispose() => Renderer.Dispose();
    }

    // Both controls draw the SAME shape, so their icons are instances of one mesh in one shared array; each has its own brush.
    private static Scene NewScene(int toolbarIcons, int marginIcons, bool clipMargin = false)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };
        var shape = new RectangleGeometry { Rect = new Rect(2, 0, 6, 10) };
        var marginBrush = new SolidColorBrush(Colors.White);

        var toolbar = new Icons(Icon(shape, new SolidColorBrush(Colors.White)), toolbarIcons);
        var margin = new Icons(Icon(shape, marginBrush), marginIcons) { ClipToBounds = clipMargin };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(toolbar);
        row.Children.Add(margin);

        var scene = new Scene
        {
            Renderer = renderer, Root = new VisualRoot(row, Dim, Dim), Toolbar = toolbar, Margin = margin, MarginBrush = marginBrush
        };
        scene.Draw();
        return scene;
    }

    private static DrawingImage Icon(Geometry shape, Brush brush) =>
        new() { Drawing = new GeometryDrawing { Geometry = shape, Brush = brush } };

    private static byte[] Pixels(OffscreenTestRenderer renderer)
    {
        using var img = renderer.RenderTarget.ResolveTexture.ReadbackToImage();
        var bytes = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void AssertMatchesAFullWalk(Scene scene, byte[] patched, string because)
    {
        RenderDirty.MarkStructural();
        scene.Draw();

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.False, "the reference frame has to actually walk");
        Assert.That(DifferingPixels(patched, Pixels(scene.Renderer)), Is.Zero, because);
    }

    private static int DifferingPixels(byte[] a, byte[] b)
    {
        var count = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3])
            {
                count++;
            }
        }

        return count;
    }

    [TestCase(1, TestName = "OneMoreIcon_SplicedIntoTheFrame_IsDrawnOnce_WhereAWalkDrawsIt")]
    [TestCase(0, TestName = "TheFirstIcon_OfAControl_SplicedIntoTheFrame_IsDrawnWhereAWalkDrawsIt_AndNowhereElse")]
    public void AnIconSplicedIntoTheFrame_IsDrawnExactlyAsAWalkDrawsIt(int before)
    {
        using var scene = NewScene(1, before);

        scene.Grow(scene.Margin);

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "a new icon is spliced in, not walked");
        AssertMatchesAFullWalk(scene, Pixels(scene.Renderer),
            "the spliced frame must show the icons where a walk puts them, with no unplaced copy at the corner");
    }

    [Test]
    public void AfterAnEarlierControlGrows_ANeighboursRecolor_LandsOnItsOwnIcon()
    {
        using var scene = NewScene(1, 1);
        scene.Grow(scene.Toolbar);
        Assume.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "precondition: the toolbar's icon is spliced in");

        scene.MarginBrush.Color = Colors.Red;
        scene.Draw();

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "a recolor is patched in place, not walked");
        AssertMatchesAFullWalk(scene, Pixels(scene.Renderer),
            "the recolor must rewrite the margin's icon where it sits now, not the toolbar's icon that moved into its old slot");
    }

    [Test]
    public void AfterAnEarlierControlGrows_ANeighbourUnderAnotherClip_GrowsWhereItsIconsAre()
    {
        using var scene = NewScene(1, 1, clipMargin: true);
        scene.Grow(scene.Toolbar);
        Assume.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "precondition: the toolbar's icon is spliced in");

        scene.Grow(scene.Margin);
        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "the margin's run moved with the edit, so it is still spliced");

        AssertMatchesAFullWalk(scene, Pixels(scene.Renderer),
            "the margin's run sits in a later flush that the toolbar's edit shifted; its icons must not overwrite the toolbar's");
    }

    [Test]
    public void AControlThatDroppedItsOutline_AndAShape_LeavesNoOldOutlineBehind()
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        var shapes = new Shapes { Count = 2, Outline = new Pen(new SolidColorBrush(Colors.Red), 3) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(shapes);
        using var scene = new Scene
        {
            Renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black },
            Root = new VisualRoot(row, Dim, Dim)
        };
        scene.Draw();

        shapes.Outline = null;
        shapes.Count = 1;
        shapes.InvalidateRender(false);
        scene.Draw();
        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "an outline-free group is spliced, not walked");

        AssertMatchesAFullWalk(scene, Pixels(scene.Renderer), "the outline the control no longer draws must not stay on the frame");
    }

    // A stroked shape cannot be re-issued into its flush (the ink list is shared), so the frame walks - but the splice has
    // to say so while validating, before it has blanked or re-issued anything for the other groups.
    [Test]
    public void AStrokedControlThatGrows_IsRefusedBeforeTheFrameIsTouched()
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        var shapes = new Shapes { Count = 2, Outline = new Pen(new SolidColorBrush(Colors.Red), 3) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(shapes);
        using var scene = new Scene
        {
            Renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black },
            Root = new VisualRoot(row, Dim, Dim)
        };
        FrameTrace.Enabled = true;
        scene.Draw();

        shapes.Count = 3;
        shapes.InvalidateRender(false);
        FrameTrace.Refuser = null;
        scene.Draw();

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.False, "a stroked group is walked, not spliced");
        Assert.That(FrameTrace.Refuser, Is.EqualTo("overlay"), "refused while validating, not halfway through the surgery");
        AssertMatchesAFullWalk(scene, Pixels(scene.Renderer), "the walk draws the third shape and its outline");
    }

    private sealed class StubResourceFactory : IResourceFactory
    {
        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => throw new NotSupportedException();
    }
}
