using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// A stretched presenter centers the label it generates from a string by layout, since text alignment only acts inside
// the label's own box.
[TestFixture]
public class GeneratedLabelCenteringTests
{
    private static (TextBlock label, ContentPresenter presenter) Row(VerticalAlignment presenterAlignment, double height)
    {
        ContentPresenter presenter = null;

        var template = new ControlTemplate(() =>
        {
            presenter = new ContentPresenter { VerticalAlignment = presenterAlignment, Content = "Mercury" };
            return new TemplateResult { RootComponent = new Border { Child = presenter } };
        });

        var item = new ListBoxItem { Height = height, Width = 200, Template = template };
        var window = new Window { Width = 300, Height = 200, Content = item };
        for (var i = 0; i < 5; i++) WindowExtension.UpdateTree(window);

        return (presenter.LogicalChildren.OfType<TextBlock>().FirstOrDefault(), presenter);
    }

    // Equal space above and below, after checking the label is its own ink height (a filling label would pass trivially).
    [Test]
    public void AStretchedRowCentersItsLabel()
    {
        var (label, presenter) = Row(VerticalAlignment.Stretch, 40);

        Assert.That(label, Is.Not.Null);

        var above = label.Bounds.Y;
        var below = presenter.Bounds.Height - label.Bounds.Y - label.Bounds.Height;

        Assert.Multiple(() =>
        {
            Assert.That(label.Bounds.Height, Is.EqualTo(label.DesiredSize.Height).Within(0.5),
                "the label was stretched, so its bounds say nothing about where the TEXT ended up");
            Assert.That(above, Is.EqualTo(below).Within(0.5),
                $"label sits {above:0.##} from the top and {below:0.##} from the bottom of a {presenter.Bounds.Height} row");
        });
    }

    /// <summary>And the presenter itself still FILLS the row - which is the reason it was stretched in the first place,
    /// so that content wanting the whole slot can have it. Only the generated label opts out.</summary>
    [Test]
    public void TheStretchedPresenterStillFillsTheRow()
    {
        var (_, presenter) = Row(VerticalAlignment.Stretch, 40);

        Assert.That(presenter.Bounds.Height, Is.EqualTo(40).Within(0.5));
    }

    /// <summary>An explicit alignment is not overridden - only Stretch is reinterpreted, because only Stretch has no
    /// meaning for a single line of text.</summary>
    [Test]
    public void AnExplicitAlignmentIsLeftAlone()
    {
        var (label, presenter) = Row(VerticalAlignment.Top, 40);

        Assert.That(label.Bounds.Y, Is.EqualTo(0).Within(0.5),
            "Top must still mean top - the presenter shrinks to the label and both sit at the top of the row");
        Assert.That(presenter.Bounds.Y, Is.EqualTo(0).Within(0.5));
    }
}
