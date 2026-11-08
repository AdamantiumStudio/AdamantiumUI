using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class ParkedVisualsReleaseTests
{
    private static readonly object Owner = new();

    [SetUp]
    [TearDown]
    public void ResetStore()
    {
        ParkedVisuals.Clear();
    }

    [Test]
    public void Release_DiscardsARequiredViewAndItsChildren()
    {
        var key = new object();
        var child = new Border();
        var view = new ParkedView { Child = child };
        ParkedVisuals.Keep(Owner, key, view);

        ParkedVisuals.ReleaseAbsent(Owner, _ => false);

        Assert.That(ParkedVisuals.TryTake(Owner, key, null, out _, out _, out _, out _), Is.False);
        Assert.That(view.IsDiscarded, Is.True);
        Assert.That(child.IsDiscarded, Is.True);
    }

    [Test]
    public void Release_KeepsWhatTheOwnerStillHolds()
    {
        var released = new object();
        var kept = new object();
        var keptView = new ParkedView();
        ParkedVisuals.Keep(Owner, released, new ParkedView());
        ParkedVisuals.Keep(Owner, kept, keptView);

        ParkedVisuals.ReleaseAbsent(Owner, key => key == kept);

        Assert.That(keptView.IsAwaitingReturn, Is.True);
        Assert.That(ParkedVisuals.TryTake(Owner, kept, null, out _, out _, out _, out _), Is.True);
        Assert.That(ParkedVisuals.TryTake(Owner, released, null, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void Release_LeavesAnotherOwnersViews()
    {
        var key = new object();
        var otherOwner = new object();
        ParkedVisuals.Keep(otherOwner, key, new ParkedView());

        ParkedVisuals.ReleaseAbsent(Owner, _ => false);

        Assert.That(ParkedVisuals.TryTake(otherOwner, key, null, out _, out _, out _, out _), Is.True);
    }

    [Test]
    public void Clear_DiscardsWhatWasKept()
    {
        var view = new ParkedView();
        ParkedVisuals.Keep(Owner, new object(), view);

        ParkedVisuals.Clear();

        Assert.That(view.IsDiscarded, Is.True);
    }

    // A theme swap rebuilds the presenter a kept view was built in. Still naming the old presenter as its templated
    // parent once taken up by the new one, the view held it - and through it the whole old template it sat in.
    [Test]
    public void AParkedViewTakenUpByAnotherPresenter_NamesThatPresenter()
    {
        var owner = new Border();
        var template = new DataTemplate(() => new TemplateResult { RootComponent = new ParkedView() });
        var page = new object();
        var first = new ContentPresenter { ContentTemplate = template, Content = page, TemplatedParent = owner };
        first.Measure(new Size<double>(100, 100));
        var view = (ParkedView)first.VisualChildren.Single();
        Assume.That(view.TemplatedParent, Is.SameAs(first));

        // Another page, through another template, as a gallery's pages are: one template would just re-point the view.
        first.ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border() });
        first.Content = new object();
        first.Measure(new Size<double>(100, 100), force: true);
        Assume.That(view.IsAwaitingReturn, "the page left is parked");

        DiscardedVisuals.Publish(first);
        while (DiscardedVisuals.Drain(64) > 0) { }

        var second = new ContentPresenter { ContentTemplate = template, Content = page, TemplatedParent = owner };
        second.Measure(new Size<double>(100, 100));

        Assert.Multiple(() =>
        {
            Assert.That(second.VisualChildren.Single(), Is.SameAs(view), "the page comes back");
            Assert.That(view.TemplatedParent, Is.SameAs(second), "it names the presenter that took it up");
        });
    }

    // Tabs bound to items share one content template. The presenter reused the page it built for the first item for the
    // second - one browser page showed every tab in turn, reloading each time - instead of keeping it for the first.
    [Test]
    public void OneTemplateForTwoItems_KeepsAPageForEach()
    {
        var template = new DataTemplate(() => new TemplateResult { RootComponent = new ParkedView() });
        var firstItem = new object();
        var secondItem = new object();
        var owner = new TabControl();
        owner.Items.Add(firstItem);
        owner.Items.Add(secondItem);
        var presenter = new ContentPresenter { ContentTemplate = template, Content = firstItem, TemplatedParent = owner };
        presenter.Measure(new Size<double>(100, 100));
        var firstPage = (ParkedView)presenter.VisualChildren.Single();

        presenter.Content = secondItem;
        presenter.Measure(new Size<double>(100, 100), force: true);
        var secondPage = (ParkedView)presenter.VisualChildren.Single();

        presenter.Content = firstItem;
        presenter.Measure(new Size<double>(100, 100), force: true);

        Assert.Multiple(() =>
        {
            Assert.That(secondPage, Is.Not.SameAs(firstPage), "the second item gets a page of its own");
            Assert.That(secondPage.DataContext, Is.SameAs(secondItem));
            Assert.That(firstPage.IsAwaitingReturn, Is.False, "the first page is taken back up");
            Assert.That(presenter.VisualChildren.Single(), Is.SameAs(firstPage), "the first item gets its page back");
            Assert.That(firstPage.DataContext, Is.SameAs(firstItem));
            Assert.That(secondPage.IsAwaitingReturn, Is.True, "the second page waits for its item");
        });
    }

    // Outside an owner of items nothing lets a kept page go with its content: one page follows the content, as before,
    // rather than a page kept for good for every content ever shown.
    [Test]
    public void OneTemplateOutsideAnOwnerOfItems_ReusesItsPage()
    {
        var template = new DataTemplate(() => new TemplateResult { RootComponent = new ParkedView() });
        var presenter = new ContentPresenter { ContentTemplate = template, Content = new object(), TemplatedParent = new Border() };
        presenter.Measure(new Size<double>(100, 100));
        var page = presenter.VisualChildren.Single();

        var next = new object();
        presenter.Content = next;
        presenter.Measure(new Size<double>(100, 100), force: true);

        Assert.Multiple(() =>
        {
            Assert.That(presenter.VisualChildren.Single(), Is.SameAs(page));
            Assert.That(((ParkedView)page).DataContext, Is.SameAs(next));
            Assert.That(ParkedVisuals.TryTake(presenter.TemplatedParent, next, null, out _, out _, out _, out _), Is.False,
                "nothing parked");
        });
    }

    [Test]
    public void MoveOwner_FilesWhatWasKeptUnderTheNewOwner()
    {
        var key = new object();
        var other = new object();
        var view = new ParkedView();
        ParkedVisuals.Keep(Owner, key, view);

        ParkedVisuals.MoveOwner(Owner, other);

        Assert.Multiple(() =>
        {
            Assert.That(ParkedVisuals.TryTake(Owner, key, null, out _, out _, out _, out _), Is.False);
            Assert.That(ParkedVisuals.TryTake(other, key, null, out var taken, out _, out _, out _), Is.True);
            Assert.That(taken, Is.SameAs(view));
        });
    }

    private class ParkedView : Border
    {
        public override NavigationCacheMode KeepAlive => NavigationCacheMode.Required;
    }
}
