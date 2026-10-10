using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Word and letter spacing and tracking on elements: inherited by the text inside them, a run's own tracking
/// taking precedence, and justified lines filling their width with spaced letters.</summary>
[TestFixture]
public class TextBlockSpacingTests
{
    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself was astonished.";

    private static TextBlock Hosted(TextBlock text, double tracking = 0)
    {
        var border = new Border { Child = text, Tracking = tracking };
        var window = new Window { Width = 900, Height = 600, Content = border };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return text;
    }

    [Test]
    public void Tracking_IsInheritedByTheText()
    {
        var plain = Hosted(new TextBlock { Text = "abc", FontSize = 20 }).Layout.GetTextData();
        var tracked = Hosted(new TextBlock { Text = "abc", FontSize = 20 }, 100).Layout.GetTextData();

        for (var i = 0; i < plain.Length; i++)
        {
            Assert.That(tracked[i].Advance - plain[i].Advance, Is.EqualTo(2).Within(1e-6));
        }
    }

    [Test]
    public void GlyphScaling_IsInheritedByTheText()
    {
        var text = new TextBlock { Text = "abc", FontSize = 20 };
        var border = new Border { Child = text, GlyphScaling = new SpacingRange(1.1, 1.1, 1.1) };
        var window = new Window { Width = 900, Height = 600, Content = border };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        Assert.That(text.Layout.GetTextData().Select(glyph => glyph.HorizontalScale), Is.All.EqualTo(1.1).Within(1e-9));
    }

    [Test]
    public void TheLastJustifiedLine_FollowsTheInheritedLastLineAlignment()
    {
        var text = new TextBlock
        {
            Text = Prose, FontSize = 20, Width = 300, TextWrapping = TextWrapping.WrapByWords,
            HorizontalTextAlignment = HorizontalTextAlignment.Justify,
        };
        var border = new Border { Child = text, LastLineAlignment = HorizontalTextAlignment.Right };
        var window = new Window { Width = 900, Height = 600, Content = border };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        var last = text.Layout.LineCount - 1;
        Assert.That(text.Layout.GetTextData().Where(glyph => glyph.LineIndex == last && glyph.Symbol != ' ')
            .Max(glyph => glyph.Rect.Right), Is.EqualTo(300).Within(1));
    }

    [Test]
    public void ARunsTracking_TakesPrecedence()
    {
        var text = new TextBlock { FontSize = 20 };
        text.Inlines.Add(new Run { Text = "ab" });
        text.Inlines.Add(new Run { Text = "cd", Tracking = -50 });
        var glyphs = Hosted(text, 100).Layout.GetTextData();
        var plain = Hosted(new TextBlock { Text = "abcd", FontSize = 20 }).Layout.GetTextData();

        Assert.That(glyphs[0].Advance - plain[0].Advance, Is.EqualTo(2).Within(1e-6));
        Assert.That(glyphs[2].Advance - plain[2].Advance, Is.EqualTo(-1).Within(1e-6));
    }

    [Test]
    public void JustifiedWithLetterSpacing_LinesFillTheWidth()
    {
        var text = Hosted(new TextBlock
        {
            Text = Prose, FontSize = 20, Width = 300, TextWrapping = TextWrapping.WrapByWords,
            HorizontalTextAlignment = HorizontalTextAlignment.Justify, WordSpacing = new SpacingRange(1, 1, 1.2),
            LetterSpacing = new SpacingRange(0, 0, 0.5),
        });
        var glyphs = text.Layout.GetTextData();

        for (var line = 0; line < text.Layout.LineCount - 1; line++)
        {
            Assert.That(glyphs.Where(glyph => glyph.LineIndex == line && glyph.Symbol != ' ').Max(glyph => glyph.Rect.Right),
                Is.EqualTo(300).Within(1), $"line {line}");
        }
    }
}
