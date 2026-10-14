using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

// An overlay window is shown on popups the manager makes for that showing. Closed, they were only taken off the layer:
// the dim behind a modal window, and the card around it, lived on - and with them whatever held them.
[TestFixture]
public class OverlayWindowReleaseTests
{
    [Test]
    public void AClosedOverlayWindow_DiscardsThePopupsItWasShownOn_ButNotItself()
    {
        var host = new Window { Width = 400, Height = 300 };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(host);
        var window = new OverlayWindow { IsModal = true };

        host.ShowOverlayWindow(window);
        var card = window.HostPopup;
        var scrim = window.ScrimPopup;
        Assume.That(card, Is.Not.Null, "shown on a card");
        Assume.That(scrim, Is.Not.Null, "with a dim behind it");

        window.Close();
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.Multiple(() =>
        {
            Assert.That(card.IsDiscarded, Is.True, "the card");
            Assert.That(scrim.IsDiscarded, Is.True, "the dim");
            Assert.That(window.IsDiscarded, Is.False, "the window is its maker's to keep or let go");
        });
    }
}
