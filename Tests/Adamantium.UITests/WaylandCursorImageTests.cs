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

    /// <summary>An Xcursor file of a cursor theme: the picture of the nominal size nearest the one wanted, its hot spot,
    /// its nominal size and its pixels as they are - premultiplied already.</summary>
    [Test]
    public void AnXcursorFile_GivesThePictureOfTheNearestNominalSize()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Xcursor((24, 32, 3, 4, 0x80402010u), (48, 64, 6, 8, 0xFF000000u)));

            var small = WaylandCursorImage.Load(path, 24);
            var large = WaylandCursorImage.Load(path, 40);

            Assert.Multiple(() =>
            {
                Assert.That((small.Width, small.NominalSize, small.HotX, small.HotY), Is.EqualTo((32, 24, 3, 4)));
                Assert.That(Pixel(small, 0, 0), Is.EqualTo(new byte[] { 0x10, 0x20, 0x40, 0x80 }));
                Assert.That((large.Width, large.NominalSize, large.HotX, large.HotY), Is.EqualTo((64, 48, 6, 8)));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A file of square images, each (nominal size, width, hot spot, the one ARGB word all its pixels are).
    private static byte[] Xcursor(params (int Nominal, int Width, int HotX, int HotY, uint Argb)[] images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("Xcur"u8);
        writer.Write(16u);
        writer.Write(0x10000u);
        writer.Write((uint)images.Length);
        var position = 16 + images.Length * 12;
        foreach (var image in images)
        {
            writer.Write(0xFFFD0002u);
            writer.Write((uint)image.Nominal);
            writer.Write((uint)position);
            position += 36 + image.Width * image.Width * 4;
        }

        foreach (var image in images)
        {
            foreach (var word in (uint[])[36, 0xFFFD0002, (uint)image.Nominal, 1, (uint)image.Width, (uint)image.Width,
                         (uint)image.HotX, (uint)image.HotY, 0])
            {
                writer.Write(word);
            }

            for (var i = 0; i < image.Width * image.Width; i++)
            {
                writer.Write(image.Argb);
            }
        }

        return stream.ToArray();
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
