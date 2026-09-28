using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.UITests;

// A discarded root whose presenter lives on must also be removed from its children, not just forgotten.
[TestFixture]
public class ContentPresenterDiscardSweepTests
{
    private static int VisualChildCount(IUIComponent c)
    {
        var n = 0;
        foreach (var _ in c.VisualChildren) n++;
        return n;
    }

    [Test]
    public void AContentDiscardedUnderALivingPresenter_LeavesNoChildBehind()
    {
        var presenter = new ContentPresenter();
        var body = new StackPanel();
        presenter.Content = body;
        presenter.Measure(new Size(100, 100));

        Assert.That(VisualChildCount(presenter), Is.EqualTo(1), "the presenter is showing it to begin with");

        // The CONTENT is destroyed while the presenter lives on - a pane closing, a template torn down under a host
        // that was not. Marked, then DRAINED: the sweep runs off the drain, not off the mark (teardown is paid for in
        // the loop's idle time, which is the whole reason the queue exists).
        DiscardedVisuals.Publish(body);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(VisualChildCount(presenter), Is.EqualTo(0),
            "a handle dropped without letting the child go leaves it in the tree, untracked and still drawn");
    }
}
