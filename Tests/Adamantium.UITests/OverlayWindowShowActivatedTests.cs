using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// An overlay window shown not activated leaves the keyboard where it was, and closing it hands nothing on; its grip
// stops at its own MinWidth and MinHeight.
[TestFixture]
public class OverlayWindowShowActivatedTests
{
    [TearDown]
    public void TearDown() => FocusManager.Focus(null);

    [Test]
    public void ShownActivatedTheWindowTakesTheKeyboard()
    {
        var (window, outside) = WindowWithAButton();
        FocusManager.Focus(outside);
        var (overlay, inside) = Overlay(showActivated: true);

        window.ShowOverlayWindow(overlay);
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(FocusManager.Focused, Is.SameAs(inside));
            Assert.That(overlay.IsActive, Is.True);
        });
        overlay.Close();
    }

    [Test]
    public void ShownNotActivatedTheKeyboardStaysWhereItWas()
    {
        var (window, outside) = WindowWithAButton();
        FocusManager.Focus(outside);
        var (overlay, _) = Overlay(showActivated: false);

        window.ShowOverlayWindow(overlay);
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(FocusManager.Focused, Is.SameAs(outside));
            Assert.That(overlay.IsActive, Is.False);
        });
        overlay.Close();
    }

    [Test]
    public void ClosingOneNeverActivatedDoesNotSendTheKeyboardToTheWindowBehindIt()
    {
        var (window, outside) = WindowWithAButton();
        var (behind, _) = Overlay(showActivated: true);
        window.ShowOverlayWindow(behind);
        Settle(window);
        FocusManager.Focus(outside);
        var (card, _) = Overlay(showActivated: false);
        window.ShowOverlayWindow(card);
        Settle(window);

        card.Close();
        Settle(window);

        Assert.That(FocusManager.Focused, Is.SameAs(outside));
        behind.Close();
    }

    [Test]
    public void ClosingTheActiveWindowDoesNotActivateACardShownNotActivated()
    {
        var (window, _) = WindowWithAButton();
        var (dialog, _) = Overlay(showActivated: true);
        window.ShowOverlayWindow(dialog);
        Settle(window);
        var (card, inCard) = Overlay(showActivated: false);
        window.ShowOverlayWindow(card);
        Settle(window);

        dialog.Close();
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(card.IsActive, Is.False);
            Assert.That(FocusManager.Focused, Is.Not.SameAs(inCard));
        });
        card.Close();
    }

    [Test]
    public void AWindowClosedWhileActiveIsShownAgainInactive()
    {
        var (window, _) = WindowWithAButton();
        var (behind, _) = Overlay(showActivated: true);
        window.ShowOverlayWindow(behind);
        var (reused, _) = Overlay(showActivated: true);
        window.ShowOverlayWindow(reused);
        Settle(window);
        reused.Close();

        reused.ShowActivated = false;
        window.ShowOverlayWindow(reused);
        Settle(window);

        Assert.That(reused.IsActive, Is.False);
        reused.Close();
        behind.Close();
    }

    [Test]
    public void CollapsingACenteredWindowKeepsItsTitleBarWhereItWas()
    {
        var (window, _) = WindowWithAButton();
        var (overlay, _) = Overlay(showActivated: true);
        overlay.Height = 300;
        window.ShowOverlayWindow(overlay);
        Settle(window);
        var top = overlay.WorldTransform.TranslationVector.Y;

        overlay.IsCollapsed = true;
        Settle(window);

        Assert.That(overlay.WorldTransform.TranslationVector.Y, Is.EqualTo(top).Within(0.5));
        overlay.Close();
    }

    [Test]
    public void TheGripStopsAtTheWindowsMinWidthAndMinHeight()
    {
        var (window, _) = WindowWithAButton();
        var (overlay, _) = Overlay(showActivated: true);
        overlay.CanResize = true;
        overlay.MinWidth = 150;
        overlay.MinHeight = 60;
        window.ShowOverlayWindow(overlay);
        Settle(window);

        overlay.ResizeCard(10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(overlay.Width, Is.EqualTo(150));
            Assert.That(overlay.Height, Is.EqualTo(60));
        });
        overlay.Close();
    }

    [Test]
    public void CollapsedItKeepsNoHeightAndExpandedItHasItsOwnBack()
    {
        var (overlay, _) = Overlay(showActivated: true);
        overlay.Height = 240;

        overlay.IsCollapsed = true;
        Assert.Multiple(() =>
        {
            Assert.That(overlay.Height, Is.NaN);
            Assert.That(overlay.MinHeight, Is.EqualTo(0));
        });

        overlay.IsCollapsed = false;
        Assert.Multiple(() =>
        {
            Assert.That(overlay.Height, Is.EqualTo(240));
            Assert.That(overlay.MinHeight, Is.EqualTo(120));
        });
    }

    [Test]
    public void ASizeSetWhileCollapsedIsTheSizeItExpandsTo()
    {
        var (overlay, _) = Overlay(showActivated: true);
        overlay.Height = 240;
        overlay.IsCollapsed = true;

        overlay.Height = 320;
        overlay.Width = 500;
        overlay.IsCollapsed = false;

        Assert.Multiple(() =>
        {
            Assert.That(overlay.Height, Is.EqualTo(320));
            Assert.That(overlay.Width, Is.EqualTo(500));
        });
    }

    [Test]
    public void CollapsingLeavesABoundHeightBound()
    {
        var panel = new StackPanel { Width = 200 };
        var (overlay, _) = Overlay(showActivated: true);
        panel.AddLogicalChild(overlay);
        overlay.SetBinding("Height", new Ancestor { AncestorType = typeof(StackPanel), Path = "Width", Logical = true });

        overlay.IsCollapsed = true;
        overlay.IsCollapsed = false;
        panel.Width = 260;
        BindingUpdateQueue.Flush();

        Assert.That(overlay.Height, Is.EqualTo(260));
    }

    // Opened at a corner, its Left and Top the room from that corner's edges of the 800x600 window, for a 320x140 card;
    // dragged, it stands where it was taken to, Left and Top its top-left as for any other.
    [TestCase(OverlayStartupLocation.TopLeft, 16, 40)]
    [TestCase(OverlayStartupLocation.TopRight, 800 - 16 - 320, 40)]
    [TestCase(OverlayStartupLocation.BottomLeft, 16, 600 - 40 - 140)]
    [TestCase(OverlayStartupLocation.BottomRight, 800 - 16 - 320, 600 - 40 - 140)]
    public void OpenedAtACorner_StandsThatFarFromItsEdges_AndDraggedStandsWhereItWasTaken(OverlayStartupLocation corner,
        double left, double top)
    {
        var (window, overlay) = ShownAt(corner);
        var opened = overlay.WorldTransform.TranslationVector;
        overlay.MoveCard(new Vector2<double>(-10, -20));
        Settle(window);
        var moved = overlay.WorldTransform.TranslationVector;

        Assert.Multiple(() =>
        {
            Assert.That(opened.X, Is.EqualTo(left).Within(0.5));
            Assert.That(opened.Y, Is.EqualTo(top).Within(0.5));
            Assert.That(moved.X, Is.EqualTo(left - 10).Within(0.5), "where it was dragged to");
            Assert.That(moved.Y, Is.EqualTo(top - 20).Within(0.5));
            Assert.That(overlay.Left, Is.EqualTo(left - 10).Within(0.5), "Left is its top-left now");
            Assert.That(overlay.Top, Is.EqualTo(top - 20).Within(0.5));
        });
        overlay.Close();
    }

    // Still at its corner, a new Left and Top from the view model move it from that corner, not to the window's top-left.
    [Test]
    public void AtTheBottomRight_ANewLeftAndTopMoveItFromThatCorner()
    {
        var (window, overlay) = ShownAt(OverlayStartupLocation.BottomRight);

        overlay.Left = 56;
        overlay.Top = 70;
        Settle(window);
        var at = overlay.WorldTransform.TranslationVector;

        Assert.Multiple(() =>
        {
            Assert.That(at.X, Is.EqualTo(800 - 56 - 320).Within(0.5));
            Assert.That(at.Y, Is.EqualTo(600 - 70 - 140).Within(0.5));
        });
        overlay.Close();
    }

    // Kept to the bottom-right, its grip grows it from its top-left, as anywhere else - not from the corner it opened at.
    [Test]
    public void ResizedAtTheBottomRight_ItsTopLeftStaysPut()
    {
        var (window, overlay) = ShownAt(OverlayStartupLocation.BottomRight);
        var before = overlay.WorldTransform.TranslationVector;

        overlay.ResizeCard(360, 120);
        Settle(window);
        var after = overlay.WorldTransform.TranslationVector;

        Assert.Multiple(() =>
        {
            Assert.That(after.X, Is.EqualTo(before.X).Within(0.5));
            Assert.That(after.Y, Is.EqualTo(before.Y).Within(0.5));
        });
        overlay.Close();
    }

    // Collapsed at the bottom-left, its title bar stays where it was instead of dropping to the window's bottom edge.
    [Test]
    public void CollapsedAtTheBottomLeft_KeepsItsTitleBarWhereItWas()
    {
        var (window, overlay) = ShownAt(OverlayStartupLocation.BottomLeft);
        var top = overlay.WorldTransform.TranslationVector.Y;

        overlay.IsCollapsed = true;
        Settle(window);

        Assert.That(overlay.WorldTransform.TranslationVector.Y, Is.EqualTo(top).Within(0.5));
        overlay.Close();
    }

    // Collapsed at the top-right, its title bar does not move, so it keeps to that corner - and follows it as the window
    // resizes - where at the bottom-right it is placed by its top-left.
    [TestCase(OverlayStartupLocation.TopRight, PlacementCorner.TopRight)]
    [TestCase(OverlayStartupLocation.BottomRight, PlacementCorner.TopLeft)]
    public void Collapsed_KeepsToATopCorner_AndLeavesABottomOne(OverlayStartupLocation opensAt, PlacementCorner keptTo)
    {
        var (window, overlay) = ShownAt(opensAt);
        var left = overlay.WorldTransform.TranslationVector.X;

        overlay.IsCollapsed = true;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(overlay.HostPopup.RelativeCorner, Is.EqualTo(keptTo));
            Assert.That(overlay.WorldTransform.TranslationVector.X, Is.EqualTo(left).Within(0.5));
        });
        overlay.Close();
    }

    // Collapsed before it is first laid out, a card at the bottom-right has no place yet to keep: it stays at its corner
    // instead of jumping to the window's top-left.
    [Test]
    public void CollapsedBeforeItsFirstLayout_StaysAtItsCorner()
    {
        var (window, _) = WindowWithAButton();
        var (overlay, _) = Overlay(showActivated: false);
        overlay.Width = 320;
        overlay.StartupLocation = OverlayStartupLocation.BottomRight;
        overlay.Left = 16;
        overlay.Top = 40;
        window.ShowOverlayWindow(overlay);

        overlay.IsCollapsed = true;
        Settle(window);
        var at = overlay.WorldTransform.TranslationVector;

        Assert.Multiple(() =>
        {
            Assert.That(at.X, Is.EqualTo(800 - 16 - 320).Within(0.5));
            Assert.That(at.Y + overlay.RenderSize.Height, Is.EqualTo(600 - 40).Within(0.5));
        });
        overlay.Close();
    }

    // A pinned window stays over the others, but not over a modal one: a dialog that holds the window is never covered -
    // also when the pinned window is pressed after it opened.
    [Test]
    public void AModalWindow_StaysOverAPinnedOne()
    {
        var (window, _) = WindowWithAButton();
        var (pinned, _) = Overlay(showActivated: true);
        pinned.IsPinned = true;
        window.ShowOverlayWindow(pinned);
        Settle(window);
        var (dialog, _) = Overlay(showActivated: true);
        dialog.IsModal = true;
        window.ShowOverlayWindow(dialog);
        Settle(window);
        OverlayWindow[] opened = [.. OverlayWindowManager.GetFor(window).Windows];

        pinned.IsPinned = false;
        pinned.IsPinned = true;
        Settle(window);
        OverlayWindow[] repinned = [.. OverlayWindowManager.GetFor(window).Windows];

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.EqualTo(new[] { pinned, dialog }), "the dialog over the pinned window");
            Assert.That(repinned, Is.EqualTo(new[] { pinned, dialog }), "pinned again, still under the dialog");
        });
        dialog.Close();
        pinned.Close();
    }

    private static (Window Window, OverlayWindow Overlay) ShownAt(OverlayStartupLocation corner)
    {
        var (window, _) = WindowWithAButton();
        var (overlay, _) = Overlay(showActivated: false);
        overlay.Width = 320;
        overlay.Height = 140;
        overlay.StartupLocation = corner;
        overlay.Left = 16;
        overlay.Top = 40;
        window.ShowOverlayWindow(overlay);
        Settle(window);
        return (window, overlay);
    }

    private static (Window Window, Button Outside) WindowWithAButton()
    {
        var outside = new Button { Content = "Outside" };
        var panel = new StackPanel();
        panel.Children.Add(outside);
        var window = new Window { Width = 800, Height = 600, Content = panel };
        Settle(window);
        return (window, outside);
    }

    // The overlay takes the keyboard as it is arranged, on the popup layer.
    private static void Settle(Window window)
    {
        WindowExtension.UpdateTree(window);
        window.PopupLayer.UpdateLayout(new Size<double>(window.Width, window.Height));
    }

    private static (OverlayWindow Overlay, Button Inside) Overlay(bool showActivated)
    {
        var inside = new Button { Content = "Inside" };
        var overlay = new OverlayWindow
        {
            ShowActivated = showActivated,
            OpenDuration = 0,
            Template = new ControlTemplate(() => new TemplateResult { RootComponent = new StackPanel { Children = { inside } } })
        };
        return (overlay, inside);
    }
}
