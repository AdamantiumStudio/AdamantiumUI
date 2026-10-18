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
}
