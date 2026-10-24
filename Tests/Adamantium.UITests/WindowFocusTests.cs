using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

// Focus in a window's popup is that window's: kept when the window is activated - a backstage the application starts on
// takes the keyboard before its window first comes up - released when it is left, and back in the popup when it is
// activated again, not on the window's first stop behind it. Each test calls what the platform's window worker calls:
// LeaveWindow when a window is deactivated, EnterWindow when it is activated.
[TestFixture]
public class WindowFocusTests
{
    [TearDown]
    public void TearDown() => FocusManager.Focus(null);

    [Test]
    public void ActivationLeavesTheFocusInAPopupOfTheWindow()
    {
        var (window, outside, inside, _) = WindowWithPopup();
        FocusManager.Focus(inside);

        FocusManager.EnterWindow(window);

        Assert.That(FocusManager.Focused, Is.SameAs(inside));
        Assert.That(outside.IsFocused, Is.False);
    }

    [Test]
    public void AWindowSeenForTheFirstTimeStartsAtItsFirstStop()
    {
        var (window, outside, _, _) = WindowWithPopup();

        FocusManager.EnterWindow(window);

        Assert.That(FocusManager.Focused, Is.SameAs(outside));
    }

    [Test]
    public void BackFromAnotherWindowTheFocusIsInThePopupAgain()
    {
        var (window, _, inside, _) = WindowWithPopup();
        var (other, otherButton) = PlainWindow();
        FocusManager.Focus(inside);
        FocusManager.EnterWindow(window);

        FocusManager.LeaveWindow(window);
        Assert.That(FocusManager.Focused, Is.Null, "a window left lets go of the focus in its popup too");
        FocusManager.EnterWindow(other);
        Assert.That(FocusManager.Focused, Is.SameAs(otherButton));
        FocusManager.LeaveWindow(other);
        FocusManager.EnterWindow(window);

        Assert.That(FocusManager.Focused, Is.SameAs(inside));
    }

    [Test]
    public void BackFromAnotherWindowTheFocusIsInTheModalDialogAgain()
    {
        var outside = new Button { Content = "Outside" };
        var panel = new StackPanel();
        panel.Children.Add(outside);
        var window = new Window { Width = 400, Height = 300, Content = panel };
        WindowExtension.UpdateTree(window);
        var inDialog = new Button { Content = "In the dialog" };
        var dialog = new OverlayWindow
        {
            IsModal = true,
            Template = new ControlTemplate(() => new TemplateResult { RootComponent = new StackPanel { Children = { inDialog } } })
        };
        window.ShowOverlayWindow(dialog);
        WindowExtension.UpdateTree(window);
        FocusManager.Focus(inDialog);
        var (other, _) = PlainWindow();

        FocusManager.LeaveWindow(window);
        FocusManager.EnterWindow(other);
        FocusManager.LeaveWindow(other);
        FocusManager.EnterWindow(window);

        Assert.That(FocusManager.Focused, Is.SameAs(inDialog));
        dialog.Close();
    }

    [Test]
    public void AWindowWithNothingToFocusDoesNotKeepAnotherWindowsPopupFocused()
    {
        var (window, _, inside, _) = WindowWithPopup();
        FocusManager.Focus(inside);
        var empty = new Window { Width = 400, Height = 300, Content = new Border() };
        WindowExtension.UpdateTree(empty);

        FocusManager.LeaveWindow(window);
        FocusManager.EnterWindow(empty);

        Assert.That(FocusManager.Focused, Is.Not.SameAs(inside));
    }

    [Test]
    public void AnElementDisabledWhileTheWindowWasAwayIsNotFocusedAgain()
    {
        var (window, outside, inside, _) = WindowWithPopup();
        FocusManager.Focus(inside);
        FocusManager.LeaveWindow(window);
        inside.IsEnabled = false;

        FocusManager.EnterWindow(window);

        Assert.That(FocusManager.Focused, Is.SameAs(outside));
    }

    [Test]
    public void ContentSwappedWhileThePopupIsOpenIsTheWindowsToo()
    {
        var (window, _, inside, popup) = WindowWithPopup();
        var swapped = new Button { Content = "Swapped in" };
        popup.Child = new Border { Child = swapped };
        WindowExtension.UpdateTree(window);
        FocusManager.Focus(swapped);

        Assert.That(Popup.HostOf(swapped), Is.SameAs(window));
        Assert.That(Popup.HostOf(inside), Is.Null, "the content swapped out is no longer the window's");
        FocusManager.EnterWindow(window);
        Assert.That(FocusManager.Focused, Is.SameAs(swapped));
    }

    private static (Window Window, Button Button) PlainWindow()
    {
        var button = new Button { Content = "Other" };
        var panel = new StackPanel();
        panel.Children.Add(button);
        var window = new Window { Width = 400, Height = 300, Content = panel };
        WindowExtension.UpdateTree(window);
        return (window, button);
    }

    private static (Window Window, Button Outside, Button Inside, Popup Popup) WindowWithPopup()
    {
        var outside = new Button { Content = "Outside" };
        var inside = new Button { Content = "Inside" };
        var popup = new Popup { Child = new Border { Child = inside }, KeepOpen = true };
        var panel = new StackPanel();
        panel.Children.Add(outside);
        panel.Children.Add(popup);
        var window = new Window { Width = 400, Height = 300, Content = panel };
        WindowExtension.UpdateTree(window);
        popup.IsOpen = true;
        WindowExtension.UpdateTree(window);
        return (window, outside, inside, popup);
    }
}
