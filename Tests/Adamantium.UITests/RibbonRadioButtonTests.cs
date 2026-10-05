using Adamantium.UI.Controls;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Tools of a ribbon group are one of a set: the name on the command, and it does not reach past the group.</summary>
[TestFixture]
public class RibbonRadioButtonTests
{
    private static (RibbonGroup group, RibbonRadioButton select, RibbonRadioButton move, RibbonRadioButton rotate) Tools()
    {
        var select = new RibbonRadioButton { GroupName = "Tool", IsChecked = true };
        var move = new RibbonRadioButton { GroupName = "Tool" };
        var rotate = new RibbonRadioButton { GroupName = "Tool" };
        var group = new RibbonGroup();
        group.Items.Add(select);
        group.Items.Add(new RibbonButton());
        group.Items.Add(move);
        group.Items.Add(rotate);
        return (group, select, move, rotate);
    }

    [Test]
    public void APressChecksTheCommandAndClearsTheRestOfItsGroup()
    {
        var (_, select, move, rotate) = Tools();

        move.PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(move.IsChecked, Is.True);
            Assert.That(select.IsChecked, Is.False, "the tool picked before stayed checked");
            Assert.That(rotate.IsChecked, Is.False);
        });
    }

    [Test]
    public void APressOnTheCheckedCommandKeepsItChecked()
    {
        var (_, select, _, _) = Tools();

        select.PerformClick();

        Assert.That(select.IsChecked, Is.True, "a second press unchecked the only tool");
    }

    [Test]
    public void CheckingFromTheViewModelClearsTheRestToo()
    {
        var (_, select, _, rotate) = Tools();

        rotate.IsChecked = true;

        Assert.That(select.IsChecked, Is.False, "a bound IsChecked left two tools checked");
    }

    [Test]
    public void TheNameDoesNotReachPastTheGroup()
    {
        var (_, select, _, _) = Tools();
        var (_, otherSelect, otherMove, _) = Tools();

        otherMove.PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(select.IsChecked, Is.True, "a press in another group cleared this one");
            Assert.That(otherSelect.IsChecked, Is.False);
        });
    }
}
