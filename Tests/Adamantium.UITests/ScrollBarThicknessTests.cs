using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using NUnit.Framework;

namespace Adamantium.UITests;

// A scrollbar's cross-axis thickness comes from ScrollBar.BarThickness, which themes can style.
[TestFixture]
public class ScrollBarThicknessTests
{
    [Test]
    public void AVerticalBar_TakesItsWidthFromTheThemeNumber()
    {
        var bar = new ScrollBar { Orientation = Orientation.Vertical, BarThickness = 10 };

        Assert.Multiple(() =>
        {
            Assert.That(bar.Width, Is.EqualTo(10), "the fixed axis is the theme's number");
            Assert.That(double.IsNaN(bar.Height), Is.True, "...and the long axis still stretches");
        });
    }

    [Test]
    public void AHorizontalBar_TakesItsHeightFromTheThemeNumber()
    {
        var bar = new ScrollBar { Orientation = Orientation.Horizontal, BarThickness = 10 };

        Assert.Multiple(() =>
        {
            Assert.That(bar.Height, Is.EqualTo(10));
            Assert.That(double.IsNaN(bar.Width), Is.True);
        });
    }

    /// <summary>The two arrive in EITHER order - a theme's setter lands when the style attaches, which is not
    /// necessarily before the orientation is set - so both have to re-apply the result.</summary>
    [Test]
    public void TheOrderTheAxisAndTheNumberArriveIn_DoesNotMatter()
    {
        var thicknessFirst = new ScrollBar();
        thicknessFirst.BarThickness = 6;
        thicknessFirst.Orientation = Orientation.Horizontal;

        var orientationFirst = new ScrollBar();
        orientationFirst.Orientation = Orientation.Horizontal;
        orientationFirst.BarThickness = 6;

        Assert.That(thicknessFirst.Height, Is.EqualTo(orientationFirst.Height).And.EqualTo(6));
    }

    [Test]
    public void WithNoThemeSaying_ItKeepsTheConventionalThickness()
    {
        var bar = new ScrollBar { Orientation = Orientation.Vertical };

        Assert.That(bar.Width, Is.EqualTo(12), "a standalone bar still has an intrinsic size");
    }

    // The control never writes the long axis, so an authored Width survives BarThickness being applied.
    [Test]
    public void AnAuthorsLengthSurvives_AThemeSettingTheThickness()
    {
        var bar = new ScrollBar { Orientation = Orientation.Horizontal };
        bar.Width = 320;              // as the markup states it
        bar.BarThickness = 6;         // as the theme's setter lands, later

        Assert.Multiple(() =>
        {
            Assert.That(bar.Width, Is.EqualTo(320), "the length the author asked for is not the control's to clear");
            Assert.That(bar.Height, Is.EqualTo(6), "...and the thickness still applies");
        });
    }

    /// <summary>Flipping the axis at runtime is the one case where the control DOES have to release a size - the one it
    /// stamped itself, on the axis that has just become the long one.</summary>
    [Test]
    public void FlippingTheAxis_ReleasesOnlyTheThicknessItStamped()
    {
        var bar = new ScrollBar { Orientation = Orientation.Vertical, BarThickness = 6 };
        Assert.That(bar.Width, Is.EqualTo(6));

        bar.Orientation = Orientation.Horizontal;

        Assert.Multiple(() =>
        {
            Assert.That(double.IsNaN(bar.Width), Is.True, "the stamped thickness is released when it stops being the cross axis");
            Assert.That(bar.Height, Is.EqualTo(6), "and it moves to the new cross axis");
        });
    }
}
