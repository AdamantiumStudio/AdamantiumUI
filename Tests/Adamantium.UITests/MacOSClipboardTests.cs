using System;
using System.IO;
using Adamantium.MacOS.AppKit;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Platforms.MacOS;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The macOS clipboard, on a pasteboard of the test's own: whoever sits at the computer keeps theirs.</summary>
[TestFixture]
[Platform("MacOsX")]
public class MacOSClipboardTests
{
    private IntPtr _pasteboard;
    private MacOSClipboard _clipboard;

    [SetUp]
    public void SetUp()
    {
        _pasteboard = NSPasteboard.CreateUnique();
        _clipboard = new MacOSClipboard(_pasteboard);
    }

    [TearDown]
    public void TearDown() => NSPasteboard.ReleaseGlobally(_pasteboard);

    [Test]
    public void NothingPutThere_IsNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_clipboard.HasText(), Is.False);
            Assert.That(_clipboard.GetText(), Is.Empty);
            Assert.That(_clipboard.GetData(DataFormats.Files), Is.Null);
            Assert.That(_clipboard.GetData(DataFormats.Image), Is.Null);
        });
    }

    [TestCase("plain")]
    [TestCase("Привет, 世界 👋")]
    [TestCase("two\nlines\tand\0a zero")]
    public void Text_ComesBackAsItWas(string text)
    {
        _clipboard.SetText(text);

        Assert.Multiple(() =>
        {
            Assert.That(_clipboard.HasText(), Is.True);
            Assert.That(_clipboard.GetText(), Is.EqualTo(text));
        });
    }

    [Test]
    public void EveryFormatOfAPackage_ComesBack_AndWhatWasThereGoes()
    {
        _clipboard.SetText("before");
        byte[] own = [1, 0, 2, 255];
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 13, 10, 26, 10, 1, 2, 3];
        var package = new DataPackage();
        package.Set(DataFormats.Html, "<b>жирный</b>");
        package.Set(DataFormats.Rtf, @"{\rtf1 rich}");
        package.Set(DataFormats.Image, png);
        package.Set("My format/1", own);

        _clipboard.SetData(package);

        Assert.Multiple(() =>
        {
            Assert.That(_clipboard.GetText(), Is.EqualTo("rich"), "not the text before: the system's, of the RTF");
            Assert.That(_clipboard.GetData(DataFormats.Html), Is.EqualTo("<b>жирный</b>"));
            Assert.That(_clipboard.GetData(DataFormats.Rtf), Is.EqualTo(@"{\rtf1 rich}"));
            Assert.That(_clipboard.GetData(DataFormats.Image), Is.EqualTo(png), "not encoded anew");
            Assert.That(_clipboard.GetData("My format/1"), Is.EqualTo(own));
            Assert.That(_clipboard.Contains("My format/2"), Is.False);
        });
    }

    [Test]
    public void Files_ComeBack_WithTheirTextBesides()
    {
        var first = Path.Combine(Path.GetTempPath(), "adamantium clipboard файл.txt");
        var second = Path.Combine(Path.GetTempPath(), "adamantium-clipboard-2.txt");
        File.WriteAllText(first, string.Empty);
        File.WriteAllText(second, string.Empty);
        try
        {
            var package = new DataPackage();
            package.Set(DataFormats.Files, new[] { first, second });
            package.Set(DataFormats.Text, "two files");

            _clipboard.SetData(package);

            Assert.Multiple(() =>
            {
                Assert.That(_clipboard.GetData(DataFormats.Files), Is.EqualTo(new[] { first, second }));
                Assert.That(_clipboard.GetText(), Is.EqualTo("two files"));
            });
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Test]
    public void APackageOfNothingThatCrosses_LeavesTheClipboardAsItWas()
    {
        _clipboard.SetText("kept");

        _clipboard.SetData(new DataPackage(new object()));

        Assert.That(_clipboard.GetText(), Is.EqualTo("kept"));
    }
}
