using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>How lines are composed, read off an element's inherited settings as one value: the blocks and boxes lay
/// their text out with it, and lay it out again when it changes.</summary>
[TestFixture]
public class TextCompositionTests
{
    private static void Update(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    [Test]
    public void AnElementsComposition_IsItsSettings_TheLastOneKeptWhileTheyHold()
    {
        var border = new Border { Hyphens = Hyphens.Auto, WordSpacing = new SpacingRange(0.7, 1, 1.5) };

        var first = border.ResolveTextComposition();
        Assert.That(first,
            Is.EqualTo(new TextComposition { Hyphens = Hyphens.Auto, WordSpacing = new SpacingRange(0.7, 1, 1.5) }));
        Assert.That(border.ResolveTextComposition(first), Is.SameAs(first));

        border.Kashidas = false;
        var second = border.ResolveTextComposition(first);
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.Kashidas, Is.False);
    }

    [Test]
    public void ASettingOnAContainer_ReachesTheLayoutOfABlock_AndAChangeLaysItOutAgain()
    {
        var block = new TextBlock { Text = "one two three", TextWrapping = TextWrapping.WrapByWords, Width = 200 };
        var host = new Border { Child = block, LastLineAlignment = HorizontalTextAlignment.Right };
        var window = new Window { Width = 600, Height = 300, Content = host };
        Update(window);
        var revision = block.Layout.Revision;

        host.LineBreaking = LineBreaking.Paragraph;
        Update(window);

        Assert.That(block.Layout.Composition, Is.EqualTo(new TextComposition
        {
            LastLineAlignment = HorizontalTextAlignment.Right, LineBreaking = LineBreaking.Paragraph,
        }));
        Assert.That(block.Layout.Revision, Is.GreaterThan(revision));
    }

    [Test]
    public void ABoxsSettings_ReachItsLayout_AndAChangeLaysItOutAgain()
    {
        var box = new TextBox { Text = "one two three", Kashidas = false };
        box.MeasureSurface(new Size<double>(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(box.Layout.Composition, Is.EqualTo(new TextComposition { Kashidas = false }));
        var revision = box.Layout.Revision;

        box.MeasureSurface(new Size<double>(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(box.Layout.Revision, Is.EqualTo(revision), "nothing changed, nothing laid out");

        box.OpticalMarginAlignment = true;
        box.MeasureSurface(new Size<double>(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(box.Layout.Composition, Is.EqualTo(new TextComposition
        {
            Kashidas = false, OpticalMarginAlignment = true,
        }));
        Assert.That(box.Layout.Revision, Is.GreaterThan(revision));
    }

    [Test]
    public void GlyphsScaledToNothing_AreRefused()
    {
        Assert.That(() => new TextBlock { GlyphScaling = new SpacingRange(0, 1, 1) }, Throws.Exception);
        Assert.That(() => new TextBlock { GlyphScaling = new SpacingRange(0.95, 1, 1.05) }, Throws.Nothing);
    }
}
