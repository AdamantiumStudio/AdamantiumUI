using System;
using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Columns and exclusions on a TextBlock: the text flows through its columns, balanced when it has no
/// height, and around the areas it is told to leave free.</summary>
[TestFixture]
public class TextBlockFramesTests
{
    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone " +
        "in her face. Close by the king's castle lay a great dark forest.";

    private static TextBlock Hosted(TextBlock text)
    {
        var window = new Window { Width = 900, Height = 600, Content = new Border { Child = text } };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return text;
    }

    [Test]
    public void Parse_ReadsAreas_AndToStringReadsBack()
    {
        var areas = ExclusionList.Parse("0,0,120,90; 240.5 200 100 100");

        Assert.That(areas, Is.EqualTo(new[] { new RectangleF(0, 0, 120, 90), new RectangleF(240.5f, 200, 100, 100) }));
        Assert.That(ExclusionList.Parse(areas.ToString()), Is.EqualTo(areas));
    }

    [TestCase("1,2,3")]
    [TestCase("0,0,-5,10")]
    [TestCase("a,b,c,d")]
    public void Parse_RejectsWhatIsNotAnArea(string text)
    {
        Assert.Throws<FormatException>(() => ExclusionList.Parse(text));
    }

    [Test]
    public void ColumnsWithoutAHeight_AreBalanced()
    {
        var text = Hosted(new TextBlock { Text = Prose, TextWrapping = TextWrapping.WrapByWords, Width = 600, Columns = 3 });
        var frames = text.Layout.Frames;
        var lines = text.Layout.LineCount;

        Assert.That(frames, Has.Count.EqualTo(3));
        Assert.That(frames[1].Left, Is.GreaterThan(frames[0].Right));
        Assert.That(text.IsOverset, Is.False);
        Assert.That(Math.Ceiling(lines / 3.0) * text.Layout.GetLine(0).Height, Is.EqualTo(frames[0].Height).Within(1.5));
    }

    [TestCase("0,60,600,80")]
    [TestCase("400,0,200,150")]
    public void BalancedColumnsAroundAnExclusion_HoldAllTheText(string exclusions)
    {
        var text = Hosted(new TextBlock
        {
            Text = Prose + " " + Prose,
            TextWrapping = TextWrapping.WrapByWords,
            Width = 600,
            Columns = 2,
            Exclusions = ExclusionList.Parse(exclusions),
        });

        Assert.That(text.IsOverset, Is.False);
    }

    [Test]
    public void ColumnsTooShort_LeaveTheRestOverset()
    {
        var text = Hosted(new TextBlock
        {
            Text = Prose,
            TextWrapping = TextWrapping.WrapByWords,
            Width = 400,
            Height = 30,
            Columns = 2,
        });

        Assert.That(text.IsOverset, Is.True);
    }

    [Test]
    public void TextFlowsAroundAnExclusion()
    {
        var text = Hosted(new TextBlock
        {
            Text = Prose,
            TextWrapping = TextWrapping.WrapByWords,
            Width = 400,
            Exclusions = ExclusionList.Parse("0,0,150,40"),
        });
        var first = text.Layout.GetTextData().Where(g => g.LineIndex == 0 && g.PositionInString >= 0);

        Assert.That(first.Min(g => g.Rect.Left), Is.GreaterThanOrEqualTo(150));
    }

    [Test]
    public void OneColumnWithoutExclusions_HasNoFrames()
    {
        var text = Hosted(new TextBlock { Text = Prose, TextWrapping = TextWrapping.WrapByWords, Width = 400 });

        Assert.That(text.Layout.Frames, Is.Null);
    }
}
