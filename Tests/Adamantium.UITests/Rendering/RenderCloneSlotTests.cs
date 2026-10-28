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

    private static Scene NewScene(bool prototypeDraws) => NewScene(prototypeDraws, i => Matrix4x4.CreateTranslation<float>(i * Step, 0, 0));

    private static Scene NewScene(bool prototypeDraws, Func<int, Matrix4x4F> place, bool checkFirst = true)
    {
        var device = GpuTestDevice.Device;
        var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
            Dim, Dim) { ClearColor = Colors.Black };
        var fill = new SolidColorBrush(new Color((byte)250, (byte)40, (byte)40, (byte)255));
        var stage = new TestControl { Bounds = new Rect(0, 0, Dim, Dim), RenderSize = new Size<double>(Dim, Dim), ClipToBounds = true };
        var prototype = new TestControl { Bounds = new Rect(0, 0, 40, 40), RenderSize = new Size<double>(40, 40) };
        var square = new TestControl { Bounds = new Rect(10, 10, 20, 20), RenderSize = new Size<double>(20, 20) };
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
            clones[i] = place(i);
        }

        prototype.RenderClones = clones;
        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Fill = fill, Square = square };
        var px = scene.Draw();
        for (var i = checkFirst ? 0 : 1; i < Copies; i++)
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

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "every copy is repainted in place, not by a walk");
        for (var i = 0; i < Copies; i++)
        {
            var (red, green) = At(px, 20 + i * Step, 20);
            Assert.That(green > 150 && red < 100, Is.True, $"copy {i} is still the old color: red={red} green={green}");
        }
    }

    // A second repaint finds every copy's records where the first one left them.
    [Test]
    public void ARecolorInPlace_ReachesEveryCopy_Twice()
    {
        using var scene = NewScene(false);

        scene.Fill.Color = new Color((byte)40, (byte)220, (byte)40, (byte)255);
        scene.Draw();
        scene.Fill.Color = new Color((byte)120, (byte)120, (byte)120, (byte)255);
        var px = scene.Draw();

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "the second repaint is in place too");
        for (var i = 0; i < Copies; i++)
        {
            var at = (20 * Dim + 20 + i * Step) * 4;
            for (var channel = 0; channel < 3; channel++)
            {
                Assert.That(px[at + channel], Is.InRange(80, 170), $"copy {i} is not the new gray in channel {channel}");
            }
        }
    }

    // The first copy out of view draws nothing, and the brush still has to know the unit for the copies that draw.
    [Test]
    public void ARecolorInPlace_ReachesTheCopiesInView_WhenTheFirstIsOutOfIt()
    {
        using var scene = NewScene(false, i => Matrix4x4.CreateTranslation<float>(i == 0 ? -1000 : i * Step, 0, 0), checkFirst: false);

        scene.Fill.Color = new Color((byte)40, (byte)220, (byte)40, (byte)255);
        var px = scene.Draw();

        for (var i = 1; i < Copies; i++)
        {
            var (red, green) = At(px, 20 + i * Step, 20);
            Assert.That(green > 150 && red < 100, Is.True, $"copy {i} is still the old color: red={red} green={green}");
        }
    }

    // A turned copy is drawn on its own, not from a record a patch can rewrite, and the body it is drawn with has to
    // paint with the brush as it is now: it was built with the color of the moment it was first turned.
    [Test]
    public void ARecolorInPlace_ReachesATurnedCopy()
    {
        Matrix4x4F Place(int i)
        {
            var move = Matrix4x4.CreateTranslation<float>(i * Step, 0, 0);
            return i != 1 ? move : Matrix4x4.CreateTranslation<float>(-20, -20, 0) * Matrix4x4.CreateRotationZ<float>(0.3f) * Matrix4x4.CreateTranslation<float>(20, 20, 0) * move;
        }

        using var scene = NewScene(false, Place);

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
