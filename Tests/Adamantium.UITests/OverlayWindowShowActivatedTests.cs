using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
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
