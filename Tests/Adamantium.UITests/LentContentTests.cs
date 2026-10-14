using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Content a control owns and only lends to its template - a menu's page and footer, a slide panel's content -
/// outlives the template: a popup torn down with it leaves that content be, and a presenter it is handed back to brings it
/// back alive.</summary>
[TestFixture]
public class LentContentTests
{
    private sealed class Holder
    {
        public Border Page { get; } = new();
    }

    private static ContentPresenter Showing(object content)
    {
        var presenter = new ContentPresenter { Content = content };
        presenter.Measure(new Size(100, 100));
        return presenter;
    }

    /// <summary>A template swap - a theme change, a menu changing its shape - throws its popup away. What a presenter in
    /// it was handed through a binding is its owner's and comes back in the new template; what was written into the
    /// presenter is the template's own and goes with it.</summary>
    [Test]
    public void ADiscardedPopup_LeavesWhatItsPresentersWereHanded_AndDiscardsItsOwn()
    {
        var holder = new Holder();
        var handed = new ContentPresenter();
        handed.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(Holder.Page)) { Source = holder });
        handed.Measure(new Size(100, 100));
        var own = new Border();
        var written = Showing(own);
        var card = new StackPanel();
        card.Children.Add(handed);
        card.Children.Add(written);
        var popup = new Popup { Child = card };
        Assume.That(handed.VisualChildren, Does.Contain(holder.Page), "the binding handed the page in");

        DiscardedVisuals.Publish(popup);
        DiscardedVisuals.Drain(int.MaxValue);
        DiscardedVisuals.Drain(int.MaxValue);

        Assert.Multiple(() =>
        {
            Assert.That(holder.Page.IsDiscarded, Is.False, "the owner's page went with the popup");
            Assert.That(own.IsDiscarded, Is.True, "the popup's own content stayed behind");
        });
    }

    [Test]
    public void ContentParkedByItsOwner_ComesBackAlive_InThePresenterItIsHandedTo()
    {
        var page = new Border();
        ParkedSubtree.Park(page);

        var presenter = Showing(page);

        Assert.Multiple(() =>
        {
            Assert.That(presenter.VisualChildren, Does.Contain(page));
            Assert.That(page.IsParked, Is.False);
        });
    }
}
