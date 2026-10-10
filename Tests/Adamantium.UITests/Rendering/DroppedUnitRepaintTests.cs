using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A unit a control stopped drawing is gone, but its brush still lists it until the next walk: a repaint of that brush must
// not write it back into a slot that has since been handed to a neighbor.
[TestFixture]
[Category("Gpu")]
public class DroppedUnitRepaintTests
{
    private const int Dim = 100;

    private sealed class Scene : IDisposable
    {
        public OffscreenTestRenderer Renderer;
        public VisualRoot Root;
        public TestControl Owner;
        public SolidColorBrush Fill;

        public byte[] Draw()
        {
            Assert.That(Renderer.RenderFrame(Root), Is.True, "off-screen frame must render");
            RenderDirty.Clear();
            using var img = Renderer.RenderTarget.ResolveTexture.ReadbackToImage();
            var bytes = new byte[(int)img.TotalSizeInBytes];
            Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
            return bytes;
        }

        public void Dispose() => Renderer.Dispose();
    }

    private static readonly Geometry Shape = new RectangleGeometry(new Rect(0, 0, 20, 20));

    private static Matrix4x4F Place(float x) => Matrix4x4F.Translation(x, 10, 0);

    private static Scene NewScene()
    {
        FrameTrace.Enabled = true;
        var device = GpuTestDevice.Device;
        var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
            Dim, Dim) { ClearColor = Colors.Black };
        var fill = new SolidColorBrush(new Color((byte)250, (byte)40, (byte)40, (byte)255));
        var stage = new TestControl { Bounds = new Rect(0, 0, Dim, Dim), RenderSize = new Size(Dim, Dim) };
        var owner = new TestControl { Bounds = new Rect(0, 0, Dim, 40), RenderSize = new Size(Dim, 40) };
        owner.RenderAction = s => s.DrawGeometry(fill, Shape, null, Place(10)).DrawGeometry(fill, Shape, null, Place(40));
        var neighbor = new TestControl { Bounds = new Rect(0, 0, Dim, 40), RenderSize = new Size(Dim, 40) };
        var blue = new SolidColorBrush(new Color((byte)40, (byte)40, (byte)250, (byte)255));
        neighbor.RenderAction = s => s.DrawGeometry(blue, Shape, null, Place(70));
        stage.Add(owner);
        stage.Add(neighbor);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Owner = owner, Fill = fill };
        scene.Draw();
        return scene;
    }

    private static (byte, byte, byte) At(byte[] px, int x, int y)
    {
        var i = (y * Dim + x) * 4;
        return (px[i], px[i + 1], px[i + 2]);
    }

    private static bool Dark(byte[] px, int x, int y)
    {
        var (a, b, c) = At(px, x, y);
        return a < 60 && b < 60 && c < 60;
    }

    [Test]
    public void ARepaintAfterAShapeWasDropped_LeavesTheNeighborAlone()
    {
        using var scene = NewScene();

        scene.Owner.RenderAction = s => s.DrawGeometry(scene.Fill, Shape, null, Place(10));
        scene.Owner.Invalidate();
        var dropped = scene.Draw();
        Assert.That(Dark(dropped, 50, 20), Is.True, "the dropped shape is gone");
        Assert.That(Dark(dropped, 80, 20), Is.False, "the neighbor is drawn");
        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True,
            $"the drop is spliced, which is what moves the neighbor (refused by {FrameTrace.Refuser})");

        scene.Fill.Color = new Color((byte)40, (byte)220, (byte)40, (byte)255);
        var repainted = scene.Draw();

        Assert.Multiple(() =>
        {
            Assert.That(Dark(repainted, 50, 20), Is.True, "the dropped shape must not come back");
            Assert.That(At(repainted, 80, 20), Is.EqualTo(At(dropped, 80, 20)), "the neighbor must keep its own record");
            Assert.That(Dark(repainted, 20, 20), Is.False, "the kept shape is drawn");
        });
    }
}
