using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// Ruby as an inline: its annotation centered over its inlines at half their size, under the line when asked, set at
// its own size, and laid out again when it changes.
[TestFixture]
public class TextRubyTests
{
    private const double FontSize = 24;
    private const string Annotated = "Tokyo";
    private const int Start = 6;

    private static (TextBlock Text, Ruby Ruby, Window Window) Hosted(string annotation = "capital")
    {
        var ruby = new Ruby { Annotation = annotation };
        ruby.Inlines.Add(new Run { Text = Annotated });
        var text = new TextBlock { FontSize = FontSize };
        text.Inlines.Add(new Run { Text = "Visit " });
        text.Inlines.Add(ruby);
        text.Inlines.Add(new Run { Text = " today" });
        var window = new Window { Width = 800, Height = 300, Content = new StackPanel { Children = { text } } };
        Update(window);
        return (text, ruby, window);
    }

    private static void Update(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static GlyphWordData[] Annotation(TextBlock text) =>
        text.Layout.GetTextData().Where(glyph => glyph.PositionInString < 0).ToArray();

    private static GlyphWordData[] Base(TextBlock text) => text.Layout.GetTextData()
        .Where(glyph => glyph.PositionInString >= Start && glyph.PositionInString < Start + Annotated.Length).ToArray();

    private static double Middle(GlyphWordData[] glyphs) =>
        (glyphs.Min(glyph => glyph.PenX) + glyphs.Max(glyph => glyph.PenX + glyph.Advance)) / 2;

    [Test]
    public void TheAnnotation_StandsCenteredOverItsInlines()
    {
        var (text, _, _) = Hosted();
        var annotation = Annotation(text);
        var bases = Base(text);

        Assert.Multiple(() =>
        {
            Assert.That(new string(annotation.Select(glyph => glyph.Symbol).ToArray()), Is.EqualTo("capital"));
            Assert.That(annotation.All(glyph => glyph.FontSize.Equals(FontSize / 2)), Is.True);
            Assert.That(Middle(annotation), Is.EqualTo(Middle(bases)).Within(0.01));
            Assert.That(annotation.Max(glyph => glyph.Rect.Bottom), Is.LessThanOrEqualTo(bases.Min(glyph => glyph.Rect.Top) + 0.5));
        });
    }

    [Test]
    public void ChangingTheRuby_LaysTheTextOutAgain()
    {
        var (text, ruby, window) = Hosted();
        var over = Annotation(text).Max(glyph => glyph.Rect.Bottom);
        ruby.RubyPosition = RubyPosition.Under;
        ruby.AnnotationFontSize = 8;
        Update(window);
        var under = Annotation(text);
        var bases = Base(text);
        ruby.Annotation = null;
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(under.Min(glyph => glyph.Rect.Top), Is.GreaterThan(over));
            Assert.That(under.Min(glyph => glyph.Rect.Top), Is.GreaterThanOrEqualTo(bases.Max(glyph => glyph.Rect.Bottom) - 0.5));
            Assert.That(under.All(glyph => glyph.FontSize.Equals(8)), Is.True);
            Assert.That(Annotation(text), Is.Empty, "no annotation, no ruby");
        });
    }

    [Test]
    public void TheAnnotation_IsShownInTheBlocksTextTransform()
    {
        var (text, _, window) = Hosted();
        text.TextTransform = TextTransform.Uppercase;
        Update(window);

        Assert.That(new string(Annotation(text).Select(glyph => glyph.Symbol).ToArray()), Is.EqualTo("CAPITAL"));
    }

    [Test]
    public void ABlockEndingInARuby_IsAsWideAsItsAnnotation()
    {
        var ruby = new Ruby { Annotation = "averyveryverylongannotation" };
        ruby.Inlines.Add(new Run { Text = "a" });
        var text = new TextBlock { FontSize = FontSize };
        text.Inlines.Add(ruby);
        Update(new Window { Width = 800, Height = 300, Content = new StackPanel { Children = { text } } });

        Assert.That(text.DesiredSize.Width, Is.GreaterThanOrEqualTo(Annotation(text).Max(glyph => glyph.Rect.Right) - 0.5));
    }

    [Test]
    public void TheBlock_GrowsToHoldTheAnnotation()
    {
        var plain = new TextBlock { FontSize = FontSize, Text = "Visit Tokyo today" };
        Update(new Window { Width = 800, Height = 300, Content = new StackPanel { Children = { plain } } });
        var (text, _, _) = Hosted();

        Assert.That(text.DesiredSize.Height, Is.GreaterThan(plain.DesiredSize.Height + FontSize / 4));
    }
}
