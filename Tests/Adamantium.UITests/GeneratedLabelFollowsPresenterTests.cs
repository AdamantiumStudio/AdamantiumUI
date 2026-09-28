using System.Linq;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// A label generated from a string keeps following the presenter's current brush, including inherited changes.
[TestFixture]
public class GeneratedLabelFollowsPresenterTests
{
    private static (ContentPresenter presenter, Window window) Hosted(Brush initial)
    {
        var presenter = new ContentPresenter { Content = "hello", Foreground = initial };
        var window = new Window { Width = 200, Height = 100, Content = presenter };
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        return (presenter, window);
    }

    private static TextBlock LabelOf(ContentPresenter presenter) =>
        presenter.VisualChildren.OfType<TextBlock>().FirstOrDefault();

    [Test]
    public void TheGeneratedLabel_TakesThePresentersBrush()
    {
        var (presenter, _) = Hosted(Brushes.Red);

        Assume.That(LabelOf(presenter), Is.Not.Null, "precondition: a string content generates a TextBlock");
        Assert.That(LabelOf(presenter).Foreground, Is.SameAs(Brushes.Red));
    }

    [Test]
    public void TheGeneratedLabel_FOLLOWSTheBrushWhenItChangesLater()
    {
        var (presenter, window) = Hosted(Brushes.Red);
        Assume.That(LabelOf(presenter)?.Foreground, Is.SameAs(Brushes.Red));

        presenter.Foreground = Brushes.Lime;
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(window);

        Assert.That(LabelOf(presenter).Foreground, Is.SameAs(Brushes.Lime),
            "the label has to track the presenter, not the value it was built with");
    }
}
