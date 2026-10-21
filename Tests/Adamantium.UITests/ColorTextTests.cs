using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.DrawingBoard;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Colors typed or read as text go through the engine's two readers: markup colors (a picker's hex box) and CSS
/// colors (a drawing's SVG).</summary>
[TestFixture]
public class ColorTextTests
{
    [TestCase("FF8000", 255, 128, 0, 255)]
    [TestCase(" #ff8000 ", 255, 128, 0, 255)]
    [TestCase("##80FF8000", 255, 128, 0, 128)]
    public void APickersHexBox_TakesSixOrEightDigits(string text, int r, int g, int b, int a)
    {
        Assert.That(ColorPicker.TryParseHex(text, out var color), Is.True);
        Assert.That((color.R, color.G, color.B, color.A), Is.EqualTo(((byte)r, (byte)g, (byte)b, (byte)a)));
    }

    // The box commits as it is typed: a short form on the way to six digits must not be taken.
    [TestCase("FF8")]
    [TestCase("FF80")]
    [TestCase("Tomato")]
    [TestCase("12345")]
    [TestCase("")]
    public void APickersHexBox_TakesNoShortFormNoNameAndNoOtherCountOfDigits(string text)
    {
        Assert.That(ColorPicker.TryParseHex(text, out _), Is.False);
    }

    [Test]
    public void ADrawingsColors_AreReadAsCss_NoneAndTransparentBeingNoColor()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CanvasSvgColor.Of("#f808"), Is.EqualTo(Color.FromRgba(255, 136, 0, 136)), "CSS puts alpha last");
            Assert.That(CanvasSvgColor.Of("rgb(50%, 0%, 0%)"), Is.EqualTo(Color.FromRgba(128, 0, 0, 255)));
            Assert.That(CanvasSvgColor.Of("darkgrey"), Is.EqualTo(Colors.DarkGray));
            Assert.That(CanvasSvgColor.Of("none"), Is.Null);
            Assert.That(CanvasSvgColor.Of("transparent"), Is.Null);
            Assert.That(CanvasSvgColor.Of("notacolor"), Is.Null);
        });
    }
}
