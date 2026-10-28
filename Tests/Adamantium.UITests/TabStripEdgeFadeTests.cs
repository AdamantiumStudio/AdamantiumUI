using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The strip has no arrows - it pans on the wheel - so the fade at its edge is the only thing saying there are
/// tabs past it. The state has to reach the CONTROL, where a trigger watches it for the ▾; the fade itself is the
/// scroller's own opacity mask, so the tabs dissolve into whatever lies beneath the strip.</summary>
[TestFixture]
public class TabStripEdgeFadeTests
{
    private static TabControl Strip(int tabs, double width)
    {
        var control = new TabControl();
        for (var i = 0; i < tabs; i++)
        {
            control.Items.Add(new TabItem { Header = $"tab {i}", Width = 120, MinWidth = 120, MaxWidth = 120 });
        }

        // The theme's own strip panel. Without it the default (a vertical stack) would leave a horizontal strip nothing
        // to overflow.
        control.ItemsPanel = new ItemsPanelTemplate(() =>
            new TemplateResult { RootComponent = new TabPanel { Orientation = Orientation.Horizontal } });

        control.Template = new ControlTemplate(() =>
        {
            var scroller = new TabStripScroller { Orientation = Orientation.Horizontal };
            var presenter = new ItemsPresenter();
            scroller.Child = presenter;

            var result = new TemplateResult { RootComponent = scroller };
            result.RegisterName("PART_TabStrip", scroller);
            result.RegisterName("PART_ItemsPresenter", presenter);
            return result;
        });

        ((IMeasurableComponent)control).Measure(new Size<double>(width, 40));
        ((IMeasurableComponent)control).Arrange(new Rect(0, 0, width, 40));
        return control;
    }

    [Test]
    public void AStripThatFitsFadesNeitherEdge()
    {
        var control = Strip(2, 400);

        Assert.Multiple(() =>
        {
            Assert.That(control.CanScrollTabsBack, Is.False);
            Assert.That(control.CanScrollTabsForward, Is.False);
        });
    }

    private static TabStripScroller ScrollerOf(TabControl control) => (TabStripScroller)control.GetTemplateChild("PART_TabStrip");

    [Test]
    public void TheFadeIsAMaskThatGoesOnceTheTabsFit()
    {
        var control = Strip(6, 300);
        Assert.That(ScrollerOf(control).OpacityMask, Is.InstanceOf<LinearGradientBrush>(), "overflowing: tabs fade out");

        ((IMeasurableComponent)control).Measure(new Size<double>(1000, 40));
        ((IMeasurableComponent)control).Arrange(new Rect(0, 0, 1000, 40));

        Assert.That(ScrollerOf(control).OpacityMask, Is.Null, "fitting: no mask, so no layer of its own");
    }

    [TestCase(39.0)]
    [TestCase(0.0)]
    public void APanMovesTheTabs(double fade)
    {
        var control = Strip(6, 300);
        var scroller = ScrollerOf(control);
        scroller.EdgeFadeExtent = fade;

        scroller.Pan(100);
        for (var pass = 0; pass < 3; pass++)
        {
            ((IMeasurableComponent)control).Measure(new Size<double>(300, 40));
            ((IMeasurableComponent)control).Arrange(new Rect(0, 0, 300, 40));
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(control);
        }

        Assert.That(((IUIComponent)scroller.Child).Bounds.X, Is.EqualTo(-100).Within(0.5),
            "the panel is arranged at its whole length and translated; that length is not its viewport");
    }

    [TestCase(double.PositiveInfinity)]
    [TestCase(600.0)]
    public void APanMovesTheTabsWhateverTheStripWasMeasuredWith(double measured)
    {
        var control = Strip(6, 300);
        var scroller = ScrollerOf(control);

        scroller.Pan(1000);
        for (var pass = 0; pass < 3; pass++)
        {
            ((IMeasurableComponent)control).Measure(new Size<double>(measured, 40));
            ((IMeasurableComponent)control).Arrange(new Rect(0, 0, 300, 40));
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(control);
        }

        Assert.That(((IUIComponent)scroller.Child).Bounds.X, Is.EqualTo(300 - 720).Within(0.5),
            "panned to the end of what the strip SHOWS, not of what it was measured with");
    }

    [Test]
    public void ATabAddedToAFittingStripMakesItOverflow()
    {
        var control = Strip(2, 300);
        Assert.That(control.CanScrollTabsForward, Is.False, "two tabs fit");

        for (var i = 0; i < 4; i++)
        {
            control.Items.Add(new TabItem { Header = $"more {i}", Width = 120, MinWidth = 120, MaxWidth = 120 });
        }

        for (var pass = 0; pass < 3; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(control);
            ((IMeasurableComponent)control).Measure(new Size<double>(300, 40));
            ((IMeasurableComponent)control).Arrange(new Rect(0, 0, 300, 40));
        }

        Assert.That(control.CanScrollTabsForward, Is.True,
            "the panel re-measured alone - it is a measure boundary - and the strip still has to learn its new length");
    }

    [Test]
    public void NoFadeExtentMeansNoMask()
    {
        var control = Strip(6, 300);
        ScrollerOf(control).EdgeFadeExtent = 0;
        ((IMeasurableComponent)control).Measure(new Size<double>(301, 40));
        ((IMeasurableComponent)control).Arrange(new Rect(0, 0, 301, 40));

        Assert.That(ScrollerOf(control).OpacityMask, Is.Null);
    }

    [Test]
    public void AnOverflowingStripFadesTheFarEdge()
    {
        var control = Strip(6, 300);

        Assert.Multiple(() =>
        {
            Assert.That(control.CanScrollTabsForward, Is.True, "the control never learned its strip overflows");
            Assert.That(control.CanScrollTabsBack, Is.False, "nothing has gone past the near edge yet");
        });
    }
}
