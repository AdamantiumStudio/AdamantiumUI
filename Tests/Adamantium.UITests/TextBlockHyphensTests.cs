using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Hyphens set on a container reaches the text inside it, and laying it out again follows a change.</summary>
[TestFixture]
public class TextBlockHyphensTests
{
    private static Window Host(Border host)
    {
        var window = new Window { Width = 600, Height = 300, Content = host };
        Update(window);
        return window;
    }

    private static void Update(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static int Hyphens(TextBlock text) => text.Layout.GetTextData().Count(g => g.PositionInString < 0);

    private static TextBlock Column() => new()
    {
        Text = "hyphenation hyphenation",
        Language = "en-US",
        TextWrapping = TextWrapping.WrapByWords,
        FontSize = 20,
        Width = 80,
    };

    [Test]
    public void ByDefault_WordsBreakOnlyAtSoftHyphens()
    {
        var text = Column();
        Host(new Border { Child = text });

        Assert.That(text.Hyphens, Is.EqualTo(Graphics.Fonts.Hyphens.Manual));
        Assert.That(Hyphens(text), Is.Zero);
    }

    [Test]
    public void HyphensOnAContainer_ReachesTheText_AndAChangeLaysItOutAgain()
    {
        var text = Column();
        var host = new Border { Child = text, Hyphens = Graphics.Fonts.Hyphens.Auto };
        var window = Host(host);

        Assert.That(Hyphens(text), Is.GreaterThan(0));

        host.Hyphens = Graphics.Fonts.Hyphens.None;
        Update(window);

        Assert.That(Hyphens(text), Is.Zero);
    }

    [Test]
    public void AHyphenLimitOnAContainer_EndsNoMoreLinesInARowInAHyphen()
    {
        const string prose = "Достопримечательности высокопроизводительного сельскохозяйственного производства " +
                             "неизменно привлекают внимание любознательных путешественников.";
        var text = new TextBlock
        {
            Text = prose, Language = "ru", TextWrapping = TextWrapping.WrapByWords, FontSize = 20, Width = 320,
        };
        var host = new Border { Child = text, Hyphens = Graphics.Fonts.Hyphens.Auto };
        var window = Host(host);
        Assume.That(LongestHyphenRun(text), Is.GreaterThan(1));

        host.HyphenLimit = 1;
        Update(window);

        Assert.That(text.HyphenLimit, Is.EqualTo(1));
        Assert.That(LongestHyphenRun(text), Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void AHyphenLimitBelowZero_IsRejected()
    {
        Assert.That(() => new TextBlock { HyphenLimit = -1 }, Throws.Exception);
    }

    private static int LongestHyphenRun(TextBlock text)
    {
        int longest = 0, run = 0;
        foreach (var line in text.Layout.GetTextData().GroupBy(g => g.LineIndex).OrderBy(g => g.Key))
        {
            run = line.OrderBy(g => g.PenX).Last(g => g.Symbol != ' ').Symbol == '-' ? run + 1 : 0;
            longest = System.Math.Max(longest, run);
        }

        return longest;
    }
}
