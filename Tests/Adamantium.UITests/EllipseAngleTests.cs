using Adamantium.UI.Controls.Shapes;
using NUnit.Framework;

namespace Adamantium.UITests;

// SweepAngle is drawn from StartAngle, so neither angle is held to the other.
[TestFixture]
public class EllipseAngleTests
{
    [Test]
    public void TheSweep_IsKept_WhateverTheStart()
    {
        var arc = new Ellipse { StartAngle = 270, SweepAngle = 45 };

        Assert.Multiple(() =>
        {
            Assert.That(arc.StartAngle, Is.EqualTo(270));
            Assert.That(arc.SweepAngle, Is.EqualTo(45));
        });
    }

    [TestCase(-10, 0)]
    [TestCase(400, 360)]
    public void TheSweep_IsHeldToOneTurn(double asked, double kept)
    {
        var arc = new Ellipse { StartAngle = 90, SweepAngle = asked };

        Assert.That(arc.SweepAngle, Is.EqualTo(kept));
    }
}
