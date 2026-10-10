using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A batch whose buffer is too small for a frame of fills only a batch can paint (gradients): that frame is held back, the
// screen keeping the one before it, and the next frame, its buffer grown, draws every fill.
[TestFixture]
[Category("Gpu")]
public class BatchRoomRenderTests
{
    private const int Dim = 160;
    private const int Cell = 2;
    private const int Old = 8;
    private const int New = 1600;

    private sealed class Scene : IDisposable
    {
        public OffscreenTestRenderer Renderer;
        public VisualRoot Root;
        public TestControl Newcomers;

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

    private static Brush Gradient() => new LinearGradientBrush
    {
        StartPoint = new Vector2(0, 0),
        EndPoint = new Vector2(1, 1),
        GradientStops =
        {
            new GradientStop(new Color((byte)250, (byte)60, (byte)60, (byte)255), 0),
            new GradientStop(new Color((byte)250, (byte)204, (byte)21, (byte)255), 1)
        }
    };

    private static TestControl Square(int x, int y)
    {
        var square = new TestControl { Bounds = new Rect(x, y, Cell, Cell), RenderSize = new Size(Cell, Cell) };
        square.RenderAction = s => s.DrawRectangle(Gradient(), new Rect(0, 0, Cell, Cell));
        return square;
    }

    // The newcomers' host is FIRST in paint order, so they come before the squares already shown: with no room kept for
    // those, they would be the ones a full buffer turned away.
    private static Scene NewScene()
    {
        var device = GpuTestDevice.Device;
        var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
            Dim, Dim) { ClearColor = Colors.Black };
        var stage = new TestControl { Bounds = new Rect(0, 0, Dim, Dim), RenderSize = new Size(Dim, Dim) };
        var newcomers = new TestControl { Bounds = new Rect(0, 0, Dim, Dim), RenderSize = new Size(Dim, Dim) };
        var shown = new TestControl { Bounds = new Rect(0, 0, Dim, Dim), RenderSize = new Size(Dim, Dim) };
        stage.Add(newcomers);
        stage.Add(shown);
        for (var i = 0; i < Old; i++)
        {
            shown.Add(Square(i * (Cell + 2), Dim - Cell - 1));
        }

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Newcomers = newcomers };
        scene.Draw();
        return scene;
    }

    private static bool Lit(byte[] px, int x, int y)
    {
        var i = (y * Dim + x) * 4;
        return px[i] > 100 || px[i + 1] > 100 || px[i + 2] > 100;
    }

    private static int LitNewcomers(byte[] px)
    {
        var lit = 0;
        for (var i = 0; i < New; i++)
        {
            var (x, y) = NewcomerAt(i);
            if (Lit(px, x + Cell / 2, y + Cell / 2))
            {
                lit++;
            }
        }

        return lit;
    }

    private static (int X, int Y) NewcomerAt(int i) => (i % 50 * (Cell + 1), i / 50 * (Cell + 1));

    [Test]
    public void AFrameWithoutRoom_IsHeldBack_TheNextDrawsEverything()
    {
        using var scene = NewScene();
        Assert.That(scene.Renderer.Cache.LastFrameWithheld, Is.False, "a frame that fits is shown");

        for (var i = 0; i < New; i++)
        {
            var (x, y) = NewcomerAt(i);
            scene.Newcomers.Add(Square(x, y));
        }

        scene.Draw();
        Assert.That(scene.Renderer.Cache.LastFrameWithheld, Is.True, "the buffer was too small: the frame is held back");

        var second = scene.Draw();
        Assert.That(scene.Renderer.Cache.LastFrameWithheld, Is.False);
        Assert.That(LitNewcomers(second), Is.EqualTo(New), "every newcomer is drawn by the frame after");
        for (var i = 0; i < Old; i++)
        {
            Assert.That(Lit(second, i * (Cell + 2) + Cell / 2, Dim - Cell / 2 - 1), Is.True, $"shown square {i}");
        }
    }
}
