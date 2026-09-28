using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// A selected-content host switching away from its owner's item must not discard it (discard is one-way); it still
// discards content it owned.
[TestFixture]
public class PresenterDoesNotDiscardOwnersItemsTests
{
    // A minimal owner shaped like the ribbon: an items control whose template shows the SELECTED item in a presenter.
    private sealed class Shelf : Selector
    {
        public static ControlTemplate Build() => new(() =>
        {
            var host = new ContentPresenter();
            var result = new TemplateResult { RootComponent = new Border { Child = host } };
            result.RegisterName("PART_Host", host);
            return result;
        });
    }

    private static Window _window;

    // The visual is (re)built inside MeasureOverride, so a content change only takes effect on the next pass: two
    // assignments in a row without one would test nothing at all.
    private static void Pump()
    {
        for (var i = 0; i < 5; i++) WindowExtension.UpdateTree(_window);
    }

    private static (Shelf shelf, ContentPresenter host, Border first, Border second) Hosted()
    {
        var first = new Border { Width = 20, Height = 10 };
        var second = new Border { Width = 20, Height = 10 };

        var shelf = new Shelf();
        shelf.Template = Shelf.Build();
        shelf.Items.Add(first);
        shelf.Items.Add(second);

        _window = new Window { Width = 300, Height = 200, Content = shelf };
        Pump();

        var host = (ContentPresenter)shelf.GetTemplateChild("PART_Host");
        host.Content = first;
        Pump();

        return (shelf, host, first, second);
    }

    /// <summary>The bug, in one line: the item the host moved away from must still be alive.</summary>
    [Test]
    public void SwitchingAwayFromAnItemDoesNotDiscardIt()
    {
        var (_, host, first, second) = Hosted();

        host.Content = second;
        Pump();

        Assert.That(first.IsDiscarded, Is.False,
            "the owner still holds this item and will show it again - discarded, it can never draw");
    }

    /// <summary>And it can come BACK: the whole point of not discarding it.</summary>
    [Test]
    public void AnItemShownAgainIsAliveAndParented()
    {
        var (_, host, first, second) = Hosted();

        host.Content = second;
        Pump();
        host.Content = first;
        Pump();

        Assert.Multiple(() =>
        {
            Assert.That(first.IsDiscarded, Is.False);
            Assert.That(first.LogicalParent, Is.SameAs(host));
        });
    }

    /// <summary>The other half, and the reason the announcement exists: content the presenter was GIVEN - not one of
    /// the owner's items - is still announced when it is swapped out, or a whole discarded view goes on holding its
    /// subscriptions.</summary>
    [Test]
    public void ContentThatIsNotAnItemIsStillDiscarded()
    {
        var (_, host, _, _) = Hosted();

        var guest = new Border { Width = 20, Height = 10 };
        host.Content = guest;
        Pump();
        host.Content = null;
        Pump();

        Assert.That(guest.IsDiscarded, Is.True,
            "an authored view swapped out of a host is gone - saying so is what keeps it from holding what it subscribed to");
    }
}
