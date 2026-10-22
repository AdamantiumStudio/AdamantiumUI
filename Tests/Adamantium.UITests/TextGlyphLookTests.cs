using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// A text's outline, shadow, glow and reflection: a run's own over its span's, a span's over the block's; a value
// changed inside one set already reaches the text.
[TestFixture]
public class TextGlyphLookTests
{
    [Test]
    public void ARunsLook_IsItsOwn_OverItsSpans()
    {
        var shadow = new TextShadow { OffsetX = 3, OffsetY = 4, Blur = 1, Color = Colors.Black };
        var inner = new Run { Text = "inner", TextGlow = new TextGlow { Radius = 2, Color = Colors.Yellow } };
        var span = new Span { TextGlow = new TextGlow { Radius = 5, Color = Colors.Red }, TextShadow = shadow };
        span.Inlines.Add(inner);
        var text = new TextBlock();
        text.Inlines.Add(new Run { Text = "plain " });
        text.Inlines.Add(span);
        Show(text);
        var attributed = text.Layout.AttributedText;
        var plain = attributed.AttributesAt(0);
        var own = attributed.AttributesAt(7);

        shadow.OffsetX = 6;
        Show(text);
        var moved = text.Layout.AttributedText.AttributesAt(7).Shadow;

        Assert.Multiple(() =>
        {
            Assert.That(plain.Glow, Is.Null);
            Assert.That(plain.Shadow, Is.Null);
            Assert.That(own.Glow, Is.EqualTo(new Adamantium.Graphics.Fonts.GlyphGlow(Colors.Yellow, 2)));
            Assert.That(own.Shadow, Is.EqualTo(new Adamantium.Graphics.Fonts.GlyphShadow(Colors.Black, 3, 4, 1)));
            Assert.That(moved.OffsetX, Is.EqualTo(6), "a value changed inside the shadow set");
        });
    }

    [Test]
    public void ABlocksLook_GoesToItsLayout_AndAChangeInsideItMovesTheRevisionOn()
    {
        var outline = new TextOutline { Width = 2, Color = Colors.Red };
        var text = new TextBlock { Text = "outlined", TextOutline = outline, TextReflection = new TextReflection() };
        Show(text);
        text.ApplyGlyphLooks();
        var revision = text.Layout.Revision;

        outline.Width = 3;
        text.ApplyGlyphLooks();

        Assert.Multiple(() =>
        {
            Assert.That(text.Layout.GlyphOutline, Is.EqualTo(new Adamantium.Graphics.Fonts.GlyphOutline(Colors.Red, 3)));
            Assert.That(text.Layout.GlyphReflection, Is.Not.Null);
            Assert.That(text.Layout.Revision, Is.GreaterThan(revision));
        });
    }

    private static void Show(TextBlock text)
    {
        var window = new Window { Width = 600, Height = 300, Content = new Border { Child = text } };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }
}
