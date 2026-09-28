using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Panels;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.UITests;

// Folding a panel against a side turns its tab labels, and the group re-measures for the turned strip's length.
[TestFixture]
public class FoldedPaneStripTests
{
    /// <summary>How much room a pane needs is its PARENT's business. Turning the label is a change of exactly that, so
    /// it has to invalidate the parent as well - <c>AffectsMeasure</c> alone only re-measures the pane itself, and the
    /// strip around it goes on reporting the width it worked out for text lying flat.</summary>
    [Test]
    public void TurningAPanesLabel_InvalidatesTheStripAroundIt()
    {
        var pane = new Pane { Header = "Inspector" };
        var strip = new StackPanel { Orientation = Orientation.Vertical };
        strip.Children.Add(pane);

        strip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(strip.IsMeasureValid, Is.True, "measured once, so the change below is the only thing under test");

        pane.LabelRotation = PaneLabelRotation.Left;

        Assert.That(strip.IsMeasureValid, Is.False,
            "a turned label changes what the pane asks for, and only the parent can act on that");
    }
}
