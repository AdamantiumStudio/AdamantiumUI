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
    private static Border Opened(Popup popup, double windowWidth = 800)
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

        var window = new Window { Width = windowWidth, Height = 600, Content = host };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        popup.IsOpen = true;
        window.PopupLayer.UpdateLayout(new Size(windowWidth, 600));
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

    // A submenu at the window's right edge opens to the left of its row, not pushed back over it; its offset away from
    // the row is kept away from it on either side, and counts toward the room it needs.
    [TestCase(PlacementMode.Right, true, 0, 330, 140)]
    [TestCase(PlacementMode.Right, false, 0, 330, 270)]
    [TestCase(PlacementMode.Right, true, 4, 330, 136)]
    [TestCase(PlacementMode.Right, true, 4, 366, 304)]
    [TestCase(PlacementMode.Right, true, 8, 366, 132)]
    [TestCase(PlacementMode.Left, true, -4, 800, 136)]
    public void BesideTheTarget_WithNoRoomOnItsSide_TheCardFlipsAcross(PlacementMode side, bool flip, double offset,
        double windowWidth, double left)
    {
        var popup = new Popup { Placement = side, FlipToFit = flip, HorizontalOffset = offset };
        var card = Opened(popup, windowWidth);

        Assert.That(card.Bounds.X, Is.EqualTo(left));
    }

    // A target zoomed by a layout transform is drawn twice its size: the card opens under its drawn edge, not under the
    // box it was laid out in.
    [Test]
    public void UnderAScaledTarget_TheCardOpensAtItsDrawnEdge()
    {
        var card = OpenedAtScaledTarget(new Popup { Placement = PlacementMode.Bottom, PlacementAlignment = PlacementAlignment.End });

        Assert.That(card.Bounds.Y, Is.EqualTo(190), "under the target's drawn bottom");
        Assert.That(card.Bounds.X, Is.EqualTo(340), "its end at the target's drawn right");
    }

    // A menu opened at a point of a zoomed target - a right click on a zoomed page - opens where that point is drawn.
    [Test]
    public void AtAPointOfAScaledTarget_TheCardOpensWhereThePointIsDrawn()
    {
        var card = OpenedAtScaledTarget(new Popup
        {
            Placement = PlacementMode.Relative, HorizontalOffset = 10, VerticalOffset = 5
        });

        Assert.That(card.Bounds.X, Is.EqualTo(220));
        Assert.That(card.Bounds.Y, Is.EqualTo(160));
    }

    // A 100 x 20 target at (200, 150) drawn at twice its size; the card is 60 x 80.
    private static Border OpenedAtScaledTarget(Popup popup)
    {
        var target = new Border { Width = 100, Height = 20 };
        var holder = new Grid
        {
            Margin = new Thickness(200, 150, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            LayoutTransform = new Adamantium.UI.Core.Media.Transform { ScaleX = 2, ScaleY = 2 }
        };
        holder.Children.Add(target);
        var card = new Border { Width = 60, Height = 80 };
        popup.PlacementTarget = target;
        popup.Child = card;
        var host = new Grid();
        host.Children.Add(holder);
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
    public void ALeftCardWithNoRoomOnItsSide_FlipsToTheRight()
    {
        var target = new Border
        {
            Width = 100, Height = 20, Margin = new Thickness(30, 150, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        var card = new Border { Width = 60, Height = 80 };
        var popup = new Popup
        {
            Placement = PlacementMode.Left, FlipToFit = true, HorizontalOffset = -4, PlacementTarget = target, Child = card
        };
        Show(popup, target);

        Assert.That(card.Bounds.X, Is.EqualTo(134));
    }

    // A submenu of a submenu that opened leftward opens leftward too, rather than back over the menu it came from.
    [Test]
    public void ASubmenuOfAFlippedSubmenu_GoesOnTheSameWay()
    {
        var target = new Border
        {
            Width = 100, Height = 20, Margin = new Thickness(200, 150, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        var row = new Border();
        var inner = new Border { Width = 60, Height = 40 };
        var innerPopup = new Popup { Placement = PlacementMode.Right, FlipToFit = true, PlacementTarget = row, Child = inner };
        var card = new Grid { Width = 120, Height = 80 };
        card.Children.Add(row);
        card.Children.Add(innerPopup);
        var popup = new Popup { Placement = PlacementMode.Right, FlipToFit = true, PlacementTarget = target, Child = card };
        var window = Show(popup, target, 400);

        innerPopup.IsOpen = true;
        for (var i = 0; i < 3; i++)
        {
            window.PopupLayer.UpdateLayout(new Size(400, 600));
        }

        Assert.Multiple(() =>
        {
            Assert.That(card.Bounds.X, Is.EqualTo(80));
            Assert.That(inner.Bounds.X, Is.EqualTo(20));
        });
    }

    private static Window Show(Popup popup, Border target, double windowWidth = 800)
    {
        var host = new Grid();
        host.Children.Add(target);
        host.Children.Add(popup);

        var window = new Window { Width = windowWidth, Height = 600, Content = host };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        popup.IsOpen = true;
        window.PopupLayer.UpdateLayout(new Size(windowWidth, 600));
        return window;
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
