using System;
using System.IO;
using Adamantium.UI.Platforms.Wayland;
using NUnit.Framework;

namespace Adamantium.UITests;

public class WaylandCursorImageTests
{
    [Test]
    public void AShippedCursor_LoadsAt32Pixels_WithItsHotSpot()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", "dragcopy.cur");
        if (!File.Exists(path))
        {
            Assert.Ignore("dragcopy.cur is not next to the tests.");
        }

        var image = WaylandCursorImage.Load(path, 32);

        Assert.That(image, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(image.Width, Is.EqualTo(32));
            Assert.That(image.Height, Is.EqualTo(32));
            Assert.That((image.HotX, image.HotY), Is.EqualTo((0, 0)));
            Assert.That(image.Pixels, Has.Some.Not.EqualTo(0), "something is drawn");
        });
    }

    [Test]
    public void AOneBitCursor_TakesItsTransparencyFromTheMask()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, OneBitCursor(hotX: 5, hotY: 7));

            var image = WaylandCursorImage.Load(path, 32);

            Assert.That(image, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That((image.HotX, image.HotY), Is.EqualTo((5, 7)));
                Assert.That(Pixel(image, 0, 0), Is.EqualTo(new byte[] { 255, 255, 255, 255 }), "the top row is white, shown");
                Assert.That(Pixel(image, 0, 31), Is.EqualTo(new byte[] { 0, 0, 0, 0 }), "the bottom row is masked out");
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] Pixel(WaylandCursorImage image, int x, int y) => image.Pixels.AsSpan((y * image.Width + x) * 4, 4).ToArray();

    private static byte[] OneBitCursor(int hotX, int hotY)
    {
        const int size = 32;
        const int stride = 4;
        using var data = new MemoryStream();
        using var w = new BinaryWriter(data);
        w.Write((ushort)0);
        w.Write((ushort)2);
        w.Write((ushort)1);
        var dibLength = 40 + 8 + stride * size * 2;
        w.Write((byte)size);
        w.Write((byte)size);
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)hotX);
        w.Write((ushort)hotY);
        w.Write(dibLength);
        w.Write(22);

        w.Write(40);
        w.Write(size);
        w.Write(size * 2);
        w.Write((ushort)1);
        w.Write((ushort)1);
        w.Write(new byte[24]);
        w.Write(new byte[] { 0, 0, 0, 0, 255, 255, 255, 0 });
        for (var row = 0; row < size; row++)
        {
            var white = row >= size / 2;
            w.Write(Row(white ? (byte)0xFF : (byte)0x00));
        }

        for (var row = 0; row < size; row++)
        {
            var masked = row < size / 2;
            w.Write(Row(masked ? (byte)0xFF : (byte)0x00));
        }

        return data.ToArray();
    }

    private static byte[] Row(byte value) => [value, value, value, value];
}
