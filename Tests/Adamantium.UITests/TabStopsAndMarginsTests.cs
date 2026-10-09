using System;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Tab stops as markup writes them, and tab stops and optical margins set on a container reaching the text
/// inside it.</summary>
[TestFixture]
public class TabStopsAndMarginsTests
{
    private static void Update(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    [Test]
    public void Parse_ReadsPositionsAlignmentsLeadersAndSeparators()
    {
        var stops = TabStopList.Parse("120, 300 Right Leader=., 400 decimal AlignOn=',', 500 Center Leader=' -'");

        Assert.That(stops, Is.EqualTo(new[]
        {
            new TabStop(120),
            new TabStop(300, TabAlignment.Right, "."),
            new TabStop(400, TabAlignment.Decimal, alignOn: ','),
            new TabStop(500, TabAlignment.Center, " -"),
        }));
    }

    [Test]
    public void ToString_IsReadBackByParse()
    {
        var stops = TabStopList.Parse("120.5, 300 Right Leader=., 400 Decimal AlignOn=',', 500 Center Leader=' -'");

        Assert.That(TabStopList.Parse(stops.ToString()), Is.EqualTo(stops));
    }

    [Test]
    public void Parse_SplitsAStopOnAnyWhiteSpace()
    {
        Assert.That(TabStopList.Parse("300\nRight"), Is.EqualTo(new[] { new TabStop(300, TabAlignment.Right) }));
    }

    [TestCase("x")]
    [TestCase("100 Sideways")]
    [TestCase("100 5")]
    [TestCase("100 -3")]
    [TestCase("-50")]
    [TestCase("NaN")]
    [TestCase("Infinity Leader=.")]
    [TestCase("1e400")]
    [TestCase("100 Leader=a'b, 200")]
    [TestCase("100 Decimal AlignOn=ab")]
    public void Parse_RejectsWhatIsNotATabStop(string text)
    {
        Assert.Throws<FormatException>(() => TabStopList.Parse(text));
    }

    [Test]
    public void StopsAndMarginsOnAContainer_ReachTheText()
    {
        var text = new TextBlock { Text = "a\tb" };
        var host = new Border { Child = text, TabStops = TabStopList.Parse("100"), OpticalMarginAlignment = true };
        var window = new Window { Width = 600, Height = 300, Content = host };
        Update(window);

        Assert.That(text.Layout.TabStops, Is.EqualTo(new[] { new TabStop(100) }));
        Assert.That(text.Layout.OpticalMarginAlignment, Is.True);

        host.TabStops = TabStopList.Parse("150");
        Update(window);

        Assert.That(text.Layout.TabStops, Is.EqualTo(new[] { new TabStop(150) }));
    }
}
