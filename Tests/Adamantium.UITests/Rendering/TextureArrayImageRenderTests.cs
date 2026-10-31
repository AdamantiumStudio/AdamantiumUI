using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>Layers of one <see cref="TextureArrayImage"/> filled into shapes with an <see cref="ImageBrush"/>: each shape
/// shows its own layer, turned or not, and layers past the array's first capacity arrive once it has grown.</summary>
[TestFixture]
[Category("Gpu")]
public class TextureArrayImageRenderTests
{
    private const int Dim = 160;
    private const int Side = 16;

    private static OffscreenTestRenderer _renderer;

    [OneTimeSetUp]
    public void CreateRenderer()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
            Dim, Dim)
        {
            ClearColor = Colors.Black
        };
    }

    [OneTimeTearDown]
    public void DisposeRenderer()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    private static byte[] Flat(byte r, byte g, byte b)
    {
        var pixels = new byte[Side * Side * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    private static byte[] Draw(Action<IDrawingSession> draw, Transform transform = null)
    {
        var control = new TestControl { RenderAction = draw };
        control.Bounds = new Rect(0, 0, Dim, Dim);
        control.RenderSize = new Size<double>(Dim, Dim);
        control.RenderTransform = transform;

        var root = new VisualRoot(control, Dim, Dim);
        Assert.That(_renderer.RenderFrame(root), Is.True);

        using var image = _renderer.RenderTarget.ResolveTexture.ReadbackToImage();
        var pixels = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, pixels, 0, pixels.Length);
        return pixels;
    }

    private static (byte R, byte G, byte B) ColorAt(byte[] pixels, int x, int y)
    {
        var i = (y * Dim + x) * 4;
        return (pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    [Test]
    public void EachShapeShowsItsOwnLayer()
    {
        using var atlas = new TextureArrayImage(Side, Side, 8, SurfaceFormat.B8G8R8A8.UNorm);
        var red = atlas.TakeLayer();
        var green = atlas.TakeLayer();
        var blue = atlas.TakeLayer();
        atlas.Write(red, Flat(255, 0, 0));
        atlas.Write(green, Flat(0, 255, 0));
        atlas.Write(blue, Flat(0, 0, 255));

        var pixels = Draw(s =>
        {
            s.DrawRectangle(new ImageBrush { Source = red }, new Rect(0, 0, 50, 50));
            s.DrawRectangle(new ImageBrush { Source = green }, new Rect(50, 0, 50, 50));
            s.DrawRectangle(new ImageBrush { Source = blue }, new Rect(100, 0, 50, 50));
        });

        Assert.Multiple(() =>
        {
            Assert.That(ColorAt(pixels, 25, 25), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
            Assert.That(ColorAt(pixels, 75, 25), Is.EqualTo(((byte)0, (byte)255, (byte)0)));
            Assert.That(ColorAt(pixels, 125, 25), Is.EqualTo(((byte)0, (byte)0, (byte)255)));
        });
    }

    [Test]
    public void ATurnedShapeStillShowsItsLayer()
    {
        using var atlas = new TextureArrayImage(Side, Side, 8, SurfaceFormat.B8G8R8A8.UNorm);
        var first = atlas.TakeLayer();
        var second = atlas.TakeLayer();
        atlas.Write(first, Flat(255, 0, 0));
        atlas.Write(second, Flat(0, 255, 0));

        var pixels = Draw(s => s.DrawRectangle(new ImageBrush { Source = second }, new Rect(40, 40, 80, 80)),
            new Transform { RotationAngle = 30, RotationCenterX = Dim / 2.0, RotationCenterY = Dim / 2.0 });

        Assert.That(ColorAt(pixels, Dim / 2, Dim / 2), Is.EqualTo(((byte)0, (byte)255, (byte)0)));
    }

    [Test]
    public void LayersPastTheFirstCapacityArriveOnceTheArrayHasGrown()
    {
        using var atlas = new TextureArrayImage(Side, Side, 64, SurfaceFormat.B8G8R8A8.UNorm);
        ImageLayer last = null;
        for (var i = 0; i < 20; i++)
        {
            last = atlas.TakeLayer();
            atlas.Write(last, i == 19 ? Flat(0, 0, 255) : Flat(40, 40, 40));
        }

        var pixels = Draw(s => s.DrawRectangle(new ImageBrush { Source = last }, new Rect(0, 0, 50, 50)));

        Assert.Multiple(() =>
        {
            Assert.That(last.Index, Is.EqualTo(19));
            Assert.That(ColorAt(pixels, 25, 25), Is.EqualTo(((byte)0, (byte)0, (byte)255)));
        });
    }

    [Test]
    public void MoreLayerFillsThanTheBatchFirstHoldsAreAllDrawn()
    {
        const int across = 20;
        const int cell = Dim / across;
        using var atlas = new TextureArrayImage(Side, Side, 512, SurfaceFormat.B8G8R8A8.UNorm);
        var brushes = new List<ImageBrush>();
        for (var i = 0; i < across * across; i++)
        {
            var layer = atlas.TakeLayer();
            atlas.Write(layer, Flat(0, 255, 0));
            brushes.Add(new ImageBrush { Source = layer });
        }

        byte[] pixels = null;
        var drawn = 0;
        for (var frame = 0; frame < 3 && drawn < brushes.Count; frame++)
        {
            pixels = Draw(s =>
            {
                for (var i = 0; i < brushes.Count; i++)
                {
                    s.DrawRectangle(brushes[i], new Rect(i % across * cell, i / across * cell, cell, cell));
                }
            });
            drawn = Enumerable.Range(0, brushes.Count)
                .Count(i => ColorAt(pixels, i % across * cell + cell / 2, i / across * cell + cell / 2).G > 200);
        }

        Assert.That(drawn, Is.EqualTo(brushes.Count), "400 layer fills, past the 256 the batch first holds");
    }

    [Test]
    public void ALayerGivenBackTwiceIsTakenOnce()
    {
        using var atlas = new TextureArrayImage(Side, Side, 8, SurfaceFormat.B8G8R8A8.UNorm);
        var layer = atlas.TakeLayer();

        atlas.ReleaseLayer(layer);
        atlas.ReleaseLayer(layer);

        Assert.Multiple(() =>
        {
            Assert.That(atlas.TakeLayer(), Is.SameAs(layer));
            Assert.That(atlas.TakeLayer(), Is.Not.SameAs(layer), "the second release was ignored");
        });
    }

    [Test]
    public void OnceDrawnTheImageTakesNoMoreLayersThanTheDeviceHolds()
    {
        var factory = new DeviceResourceFactory(GpuTestDevice.Device);
        using var atlas = new TextureArrayImage(Side, Side, uint.MaxValue, SurfaceFormat.B8G8R8A8.UNorm);
        atlas.Write(atlas.TakeLayer(), Flat(1, 2, 3));

        Assert.That(atlas.GetOrCreateTexture(factory), Is.Not.Null);
        Assert.That(atlas.MaxLayers, Is.LessThanOrEqualTo(factory.MaxTextureArrayLayers));
    }

    [Test]
    public void AReleasedLayerIsTakenAgain()
    {
        using var atlas = new TextureArrayImage(Side, Side, 2, SurfaceFormat.B8G8R8A8.UNorm);
        var first = atlas.TakeLayer();
        atlas.TakeLayer();

        Assert.That(atlas.TakeLayer(), Is.Null, "two layers asked for, two taken");
        atlas.ReleaseLayer(first);
        Assert.That(atlas.TakeLayer(), Is.SameAs(first));
    }
}
