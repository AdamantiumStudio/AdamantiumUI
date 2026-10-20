using Adamantium.UI.Controls.Shapes;
using NUnit.Framework;

namespace Adamantium.UITests;

// StopAngle is drawn as the sweep from StartAngle, so neither angle is held to the other.
[TestFixture]
public class EllipseAngleTests
{
    [Test]
    public void TheSweep_IsKept_WhateverTheStart()
    {
        var arc = new Ellipse { StartAngle = 270, StopAngle = 45 };

        Assert.Multiple(() =>
        {
            Assert.That(arc.StartAngle, Is.EqualTo(270));
            Assert.That(arc.StopAngle, Is.EqualTo(45));
        });
    }

    [TestCase(-10, 0)]
    [TestCase(400, 360)]
    public void TheSweep_IsHeldToOneTurn(double asked, double kept)
    {
        var arc = new Ellipse { StartAngle = 90, StopAngle = asked };

        Assert.That(arc.StopAngle, Is.EqualTo(kept));
    }
}
