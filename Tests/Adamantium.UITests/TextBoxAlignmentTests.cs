using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls.Text;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A wrapping <see cref="TextBox"/> aligned: the caret stands where the aligned text is, justified lines
/// stretch and the last one stays put, and text that does not wrap stays at its start.</summary>
[TestFixture]
public class TextBoxAlignmentTests
{
    private const double Width = 300;

    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself was astonished.";

    private static TextBox Measured(HorizontalTextAlignment alignment, string text = Prose,
        TextWrapping wrapping = TextWrapping.WrapByWords)
    {
        var box = new TextBox { Text = text, TextWrapping = wrapping, HorizontalTextAlignment = alignment };
        box.MeasureSurface(Width);
        return box;
    }

    private static int FirstLineEnd(TextBox box)
    {
        var index = 1;
        while (index < Prose.Length && box.CaretRect(index).Y == box.CaretRect(0).Y)
        {
            index++;
        }

        return index - 1;
    }

    [Test]
    public void Justified_TheCaretFollowsTheWidenedGaps_TheLastLineStaysPut()
    {
        var left = Measured(HorizontalTextAlignment.Left);
        var justified = Measured(HorizontalTextAlignment.Justify);
        var secondWord = Prose.IndexOf(' ') + 1;
        var lastWord = Prose.LastIndexOf(' ') + 1;

        Assert.That(justified.CaretRect(0).X, Is.EqualTo(left.CaretRect(0).X).Within(0.01));
        Assert.That(justified.CaretRect(secondWord).X, Is.GreaterThan(left.CaretRect(secondWord).X + 1));
        Assert.That(justified.CaretRect(secondWord - 1).X, Is.EqualTo(left.CaretRect(secondWord - 1).X).Within(0.01),
            "the space starts where it did");
        Assert.That(justified.CaretRect(lastWord).X, Is.EqualTo(left.CaretRect(lastWord).X).Within(0.01),
            "the last line is not stretched");
    }

    [Test]
    public void Right_EndsTheLinesAtTheRightEdge()
    {
        var right = Measured(HorizontalTextAlignment.Right);
        var end = FirstLineEnd(right);

        Assert.That(right.CaretRect(end).X, Is.EqualTo(Width).Within(2), "the last letter of the line at the edge");
        Assert.That(right.CaretRect(0).X, Is.GreaterThan(1));
    }

    [TestCase(HorizontalTextAlignment.Left, 0)]
    [TestCase(HorizontalTextAlignment.Center, Width / 2)]
    [TestCase(HorizontalTextAlignment.Right, Width)]
    public void AnEmptyField_PutsTheCaretWhereTextWillStand(HorizontalTextAlignment alignment, double x)
    {
        Assert.That(Measured(alignment, string.Empty).CaretRect(0).X, Is.EqualTo(x).Within(0.01));
    }

    [TestCase(HorizontalTextAlignment.Center)]
    [TestCase(HorizontalTextAlignment.Right)]
    public void TextThatDoesNotWrap_StaysAtItsStart(HorizontalTextAlignment alignment)
    {
        var aligned = Measured(alignment, "abc   ", TextWrapping.NoWrap);
        var left = Measured(HorizontalTextAlignment.Left, "abc   ", TextWrapping.NoWrap);

        for (var index = 0; index <= 6; index++)
        {
            Assert.That(aligned.CaretRect(index).X, Is.EqualTo(left.CaretRect(index).X).Within(0.01));
        }
    }
}
