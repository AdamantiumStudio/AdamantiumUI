using Adamantium.UI.Controls;
using Adamantium.UI.Core.Automation;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A badge says its count, capped by its maximum, and a screen reader hears the same number once.</summary>
[TestFixture]
public class BadgeTests
{
    [Test]
    public void ItSaysItsCount()
    {
        var badge = new Badge { Count = 5 };

        Assert.That(badge.Text, Is.EqualTo("5"));
    }

    [Test]
    public void ACountPastTheMaximumReadsAsTheMaximumPlus()
    {
        var badge = new Badge { Count = 120 };

        Assert.That(badge.Text, Is.EqualTo("99+"));
    }

    // The maximum set AFTER the count still caps it: the text follows whichever of the two changed last.
    [Test]
    public void ALowerMaximumCapsACountAlreadyShown()
    {
        var badge = new Badge { Count = 12 };
        badge.MaxCount = 9;

        Assert.That(badge.Text, Is.EqualTo("9+"));
    }

    [Test]
    public void TheCountAtTheMaximumIsShownAsIs()
    {
        var badge = new Badge { MaxCount = 9, Count = 9 };

        Assert.That(badge.Text, Is.EqualTo("9"));
    }

    [Test]
    public void AutomationHearsTheNumberAsText()
    {
        var badge = new Badge { Count = 120 };
        var peer = badge.GetAutomationPeer();

        Assert.Multiple(() =>
        {
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Text));
            Assert.That(peer.Name, Is.EqualTo("99+"));
        });
    }

    [Test]
    public void ADotHasNoName()
    {
        var badge = new Badge { Count = 3, IsDot = true };

        Assert.That(badge.GetAutomationPeer().Name, Is.Empty);
    }
}
