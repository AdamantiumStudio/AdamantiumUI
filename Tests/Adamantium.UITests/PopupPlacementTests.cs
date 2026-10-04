using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Where a popup stands along the side of its target: centered - a tooltip - unless told to line up edge to
/// edge, as a submenu does with its row.</summary>
[TestFixture]
public class PopupPlacementTests
{
    // A 100 x 20 target at (200, 150); the card is 60 x 80.
    private static Border Opened(Popup popup)
    {
        var target = new Border
        {
            Width = 100, Height = 20, Margin = new Thickness(200, 150, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        var card = new Border { Width = 60, Height = 80 };
        popup.PlacementTarget = target;
        popup.Child = card;

        var host = new Grid();
        host.Children.Add(target);
        host.Children.Add(popup);

        var window = new Window { Width = 800, Height = 600, Content = host };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        popup.IsOpen = true;
        window.PopupLayer.UpdateLayout(new Size(800, 600));
        return card;
    }

    [Test]
    public void APopupNotToldOtherwise_IsCenteredOnTheSideOfItsTarget()
    {
        var card = Opened(new Popup { Placement = PlacementMode.Right });

        Assert.That(card.Bounds.X, Is.EqualTo(300));
        Assert.That(card.Bounds.Y, Is.EqualTo(120));
    }

    [TestCase(PlacementAlignment.Center, 120)]
    [TestCase(PlacementAlignment.Start, 150)]
    [TestCase(PlacementAlignment.End, 90)]
    public void BesideTheTarget_TheCardLinesUpDownItsSide(PlacementAlignment alignment, double top)
    {
        var card = Opened(new Popup { Placement = PlacementMode.Right, PlacementAlignment = alignment });

        Assert.That(card.Bounds.X, Is.EqualTo(300));
        Assert.That(card.Bounds.Y, Is.EqualTo(top));
    }

    [TestCase(PlacementAlignment.Center, 220)]
    [TestCase(PlacementAlignment.Start, 200)]
    [TestCase(PlacementAlignment.End, 240)]
    public void UnderTheTarget_TheCardLinesUpAcrossIt(PlacementAlignment alignment, double left)
    {
        var card = Opened(new Popup { Placement = PlacementMode.Bottom, PlacementAlignment = alignment });

        Assert.That(card.Bounds.X, Is.EqualTo(left));
        Assert.That(card.Bounds.Y, Is.EqualTo(170));
    }

    [Test]
    public void ACenteredPopup_StaysCenteredWhateverItsAlignment()
    {
        var card = Opened(new Popup { Placement = PlacementMode.Center, PlacementAlignment = PlacementAlignment.Start });

        Assert.That(card.Bounds.X, Is.EqualTo(220));
        Assert.That(card.Bounds.Y, Is.EqualTo(120));
    }

    // A card that takes what it is given: a scroller over 900 of content, under the same target, in a window of the given
    // height.
    private static (ScrollViewer card, Window window) OpenedTall(Popup popup, double windowHeight)
    {
        var target = new Border
        {
            Width = 100, Height = 20, Margin = new Thickness(200, 150, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        var card = new ScrollViewer { Width = 60, Content = new Border { Height = 900 } };
        popup.PlacementTarget = target;
        popup.Placement = PlacementMode.Bottom;
        popup.Child = card;

        var host = new Grid();
        host.Children.Add(target);
        host.Children.Add(popup);

        var window = new Window { Width = 800, Height = windowHeight, Content = host };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        popup.IsOpen = true;
        window.PopupLayer.UpdateLayout(new Size(800, windowHeight));
        return (card, window);
    }

    // The window-fit lifts a card that is too tall for the room below its target - over the target itself.
    [Test]
    public void ATallCard_IsLiftedOverItsTarget_ToStayInTheWindow()
    {
        var (card, _) = OpenedTall(new Popup(), 600);

        Assert.That(card.Bounds.Y, Is.LessThan(170));
    }

    // A drawer opening out of a strip must leave the strip in sight: it takes the room below and scrolls what is left.
    [Test]
    public void ACardThatStaysBesideItsTarget_TakesTheRoomOnItsSide()
    {
        var (card, _) = OpenedTall(new Popup { StaysBesideTarget = true }, 600);

        Assert.Multiple(() =>
        {
            Assert.That(card.Bounds.Y, Is.EqualTo(170));
            Assert.That(card.Bounds.Height, Is.EqualTo(430));
        });
    }

    [Test]
    public void TheRoomFollowsTheWindow()
    {
        var (card, window) = OpenedTall(new Popup { StaysBesideTarget = true }, 600);

        window.PopupLayer.UpdateLayout(new Size(800, 400));

        Assert.Multiple(() =>
        {
            Assert.That(card.Bounds.Y, Is.EqualTo(170));
            Assert.That(card.Bounds.Height, Is.EqualTo(230));
        });
    }
}
