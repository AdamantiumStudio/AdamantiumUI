using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The commands stacked in a group's column stand from the top, as in Word, or spread over the group's height.</summary>
[TestFixture]
public class RibbonRowAlignmentTests
{
    private const double BandHeight = 66;
    private const double CommandHeight = 16;

    // Small commands in one column, the panel inside a host the alignment can be set on.
    private static (Border Host, RibbonGroupPanel Panel) Column(int commands = 2)
    {
        var panel = new RibbonGroupPanel();
        for (var i = 0; i < commands; i++)
        {
            var command = new Border { Width = 30, Height = CommandHeight };
            Ribbon.SetMaxSize(command, RibbonSize.Small);
            panel.Children.Add(command);
        }

        var host = new Border { Child = panel };
        Lay(host);
        return (host, panel);
    }

    private static void Lay(Border host)
    {
        host.Measure(new Size<double>(200, BandHeight));
        host.Arrange(new Rect(0, 0, 200, BandHeight));
    }

    private static double[] Tops(RibbonGroupPanel panel) => panel.Children.Select(c => c.Bounds.Y).ToArray();

    [Test]
    public void ByDefault_RowsStandFromTheTop_RowSpacingApart()
    {
        var (_, panel) = Column();

        Assert.That(Tops(panel), Is.EqualTo(new[] { 0, CommandHeight + panel.RowSpacing }));
    }

    [Test]
    public void Spread_PutsTheLastRowAtTheBottom_AndSharesTheRestEvenly()
    {
        var (host, panel) = Column(3);

        Ribbon.SetRowAlignment(host, RibbonRowAlignment.Spread);
        panel.Arrange(new Rect(0, 0, panel.Bounds.Width, BandHeight));

        Assert.That(Tops(panel), Is.EqualTo(new[] { 0, 25, 50 }));
    }

    // Too little height to spread: the rows keep RowSpacing between them rather than touching.
    [Test]
    public void Spread_NeverPutsRowsCloserThanRowSpacing()
    {
        var (host, panel) = Column(4);
        panel.RowsPerColumn = 4;

        Ribbon.SetRowAlignment(host, RibbonRowAlignment.Spread);
        Lay(host);

        var tops = Tops(panel);
        Assert.That(tops[1] - tops[0], Is.EqualTo(CommandHeight + panel.RowSpacing));
    }

    // Set on the ribbon or a group, it reaches the panel inside: the panel lays out again on its own.
    [Test]
    public void TheAlignment_IsInherited_AndRelaysThePanel()
    {
        var (host, panel) = Column();

        Ribbon.SetRowAlignment(host, RibbonRowAlignment.Spread);
        Lay(host);

        Assert.That(Tops(panel), Is.EqualTo(new[] { 0, BandHeight - CommandHeight }));
    }

    [Test]
    public void BackToTop_TheRowsStandFromTheTopAgain()
    {
        var (host, panel) = Column();
        Ribbon.SetRowAlignment(host, RibbonRowAlignment.Spread);
        Lay(host);

        Ribbon.SetRowAlignment(host, RibbonRowAlignment.Top);
        Lay(host);

        Assert.That(Tops(panel), Is.EqualTo(new[] { 0, CommandHeight + panel.RowSpacing }));
    }

    // Set on the ribbon, overridden on one group: the group's own stands.
    [Test]
    public void AGroupsOwnAlignment_OutranksTheRibbons()
    {
        var (host, panel) = Column();
        var ribbon = new Border();
        host.Child = null;
        ribbon.Child = host;
        host.Child = panel;
        Ribbon.SetRowAlignment(ribbon, RibbonRowAlignment.Spread);
        Ribbon.SetRowAlignment(host, RibbonRowAlignment.Top);

        ribbon.Measure(new Size<double>(200, BandHeight));
        ribbon.Arrange(new Rect(0, 0, 200, BandHeight));

        Assert.That(Tops(panel), Is.EqualTo(new[] { 0, CommandHeight + panel.RowSpacing }));
    }

    // A column of one command has nothing to spread.
    [Test]
    public void ALoneCommand_StaysAtTheTop()
    {
        var (host, panel) = Column(1);

        Ribbon.SetRowAlignment(host, RibbonRowAlignment.Spread);
        Lay(host);

        Assert.That(Tops(panel), Is.EqualTo(new[] { 0 }));
    }
}
