using System;
using System.Linq;
using System.Text;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Platforms.Windows;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The Windows clipboard carries several formats of one thing at once: text for any application, HTML for a
/// browser or a word processor, and formats of one's own under their names.</summary>
[TestFixture]
[Platform("Win")]
public class WindowsClipboardTests
{
    [Test]
    public void SeveralFormats_GoOnTheClipboardTogether_AndComeBack()
    {
        var clipboard = new WindowsClipboard();
        var own = Encoding.UTF8.GetBytes("<document>formats</document>");
        var package = new DataPackage();
        package.Set(DataFormats.Text, "formats: bold");
        package.Set(DataFormats.Html, "formats: <b>bold</b>");
        package.Set("Adamantium Test Format", own);

        clipboard.SetData(package);

        var read = clipboard.GetData("Adamantium Test Format") as byte[];
        Assert.Multiple(() =>
        {
            Assert.That(clipboard.Contains(DataFormats.Text), Is.True);
            Assert.That(clipboard.GetText(), Is.EqualTo("formats: bold"));
            Assert.That(clipboard.GetData(DataFormats.Html), Is.EqualTo("formats: <b>bold</b>"), "the fragment, no header");
            Assert.That(read?.Take(own.Length), Is.EqualTo(own), "the block may run on with zeros");
            Assert.That(read?.Skip(own.Length).All(b => b == 0), Is.True);
            Assert.That(clipboard.Contains(DataFormats.Rtf), Is.False);
        });

        clipboard.SetText("plain");
        Assert.That(clipboard.Contains("Adamantium Test Format"), Is.False, "a new copy replaces every format");
    }

    [Test]
    public void APicture_GoesOnAsItCameAndAsABitmap_AndComesBackAsItCame()
    {
        var clipboard = new WindowsClipboard();
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var package = new DataPackage();
        package.Set(DataFormats.Image, png);

        clipboard.SetData(package);
        var read = clipboard.GetData(DataFormats.Image) as byte[];

        Assert.Multiple(() =>
        {
            Assert.That(clipboard.Contains(DataFormats.Image), Is.True);
            Assert.That(read, Is.EqualTo(png), "the PNG itself, not decoded again, nor run on with zeros");
            Assert.That(Adamantium.Win32.Win32Interop.IsClipboardFormatAvailable(Adamantium.Win32.Win32Interop.CF_DIB),
                Is.True, "and a bitmap for the applications that know only that");
        });

        clipboard.SetText("plain");
        Assert.That(clipboard.Contains(DataFormats.Image), Is.False);
    }

    [Test]
    public void APictureFromAnotherApplication_LosesTheZerosItsBlockRanOnWith_AndNothingElse()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00, 0xFF, 0xD9];
        var bmp = new byte[70];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(58).CopyTo(bmp, 2);
        bmp[57] = 0x11;
        var wrongSize = (byte[])bmp.Clone();
        BitConverter.GetBytes(10).CopyTo(wrongSize, 2);
        byte[] unclosedGif = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', 0x01, 0x00, 0x00, 0x00];

        Assert.Multiple(() =>
        {
            Assert.That(OleDataBridge.Unpadded([.. png, 0, 0, 0]), Is.EqualTo(png));
            Assert.That(OleDataBridge.Unpadded([.. jpeg, 0, 0]), Is.EqualTo(jpeg));
            Assert.That(OleDataBridge.Unpadded(bmp), Is.EqualTo(bmp[..58]), "a BMP as long as its header says");
            Assert.That(OleDataBridge.Unpadded(wrongSize), Is.EqualTo(wrongSize), "a size no BMP has");
            Assert.That(OleDataBridge.Unpadded(unclosedGif), Is.EqualTo(unclosedGif), "a GIF that does not close");
        });
    }
}
