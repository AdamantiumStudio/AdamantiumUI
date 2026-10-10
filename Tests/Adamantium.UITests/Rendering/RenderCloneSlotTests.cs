using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A cloned subtree owns one record per copy, while the slot maps remember one: a repaint without a walk has to reach
// every copy, not just the one the map happens to name.
[TestFixture]
[Category("Gpu")]
public class RenderCloneSlotTests
{
    private const int Dim = 160;
    private const int Step = 50;
    private const int Copies = 3;

    private sealed class Scene : IDisposable
    {
        public OffscreenTestRenderer Renderer;
        public VisualRoot Root;
        public SolidColorBrush Fill;
        public TestControl Square;

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

    private static Scene NewScene(bool prototypeDraws)
    {
        var device = GpuTestDevice.Device;
        var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
            Dim, Dim) { ClearColor = Colors.Black };
        var fill = new SolidColorBrush(new Color((byte)250, (byte)40, (byte)40, (byte)255));
        var stage = new TestControl { Bounds = new Rect(0, 0, Dim, Dim), RenderSize = new Size(Dim, Dim) };
        var prototype = new TestControl { Bounds = new Rect(0, 0, 40, 40), RenderSize = new Size(40, 40) };
        var square = new TestControl { Bounds = new Rect(10, 10, 20, 20), RenderSize = new Size(20, 20) };
        square.RenderAction = s => s.DrawRectangle(fill, new Rect(0, 0, 20, 20));
        if (prototypeDraws)
        {
            prototype.RenderAction = s => s.DrawRectangle(fill, new Rect(10, 10, 20, 20));
            square.RenderAction = null;
        }

        prototype.Add(square);
        stage.Add(prototype);
        var clones = new Matrix4x4F[Copies];
        for (var i = 0; i < Copies; i++)
        {
            clones[i] = Matrix4x4F.Translation(i * Step, 0, 0);
        }

        prototype.RenderClones = clones;
        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Fill = fill, Square = square };
        var px = scene.Draw();
        for (var i = 0; i < Copies; i++)
        {
            var (red, green) = At(px, 20 + i * Step, 20);
            Assert.That(red > 150 && green < 100, Is.True, $"copy {i} is drawn red at its own place: red={red} green={green}");
        }

        return scene;
    }

    private static (byte Red, byte Green) At(byte[] px, int x, int y)
    {
        var i = (y * Dim + x) * 4;
        return (Math.Max(px[i], px[i + 2]), px[i + 1]);
    }

    [TestCase(false, TestName = "ARecolorInPlace_ReachesEveryCopy_OfADescendant")]
    [TestCase(true, TestName = "ARecolorInPlace_ReachesEveryCopy_OfThePrototype")]
    public void ARecolorInPlace_ReachesEveryCopy(bool prototypeDraws)
    {
        using var scene = NewScene(prototypeDraws);

        scene.Fill.Color = new Color((byte)40, (byte)220, (byte)40, (byte)255);
        var px = scene.Draw();

        for (var i = 0; i < Copies; i++)
        {
            var (red, green) = At(px, 20 + i * Step, 20);
            Assert.That(green > 150 && red < 100, Is.True, $"copy {i} is still the old color: red={red} green={green}");
        }
    }

    [Test]
    public void AChildThatDrawsMore_DrawsItInEveryCopy()
    {
        using var scene = NewScene(false);
        var green = new SolidColorBrush(new Color((byte)40, (byte)220, (byte)40, (byte)255));

        scene.Square.RenderAction = s => s.DrawRectangle(scene.Fill, new Rect(0, 0, 20, 20))
            .DrawRectangle(green, new Rect(5, 5, 10, 10));
        scene.Square.Invalidate();
        var px = scene.Draw();

        for (var i = 0; i < Copies; i++)
        {
            var (r, g) = At(px, 20 + i * Step, 20);
            Assert.That(g > 150 && r < 100, Is.True, $"copy {i} has no second shape: red={r} green={g}");
        }
    }
}
