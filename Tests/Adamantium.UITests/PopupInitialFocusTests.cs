using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A card opened by a shortcut takes the keyboard on the field it names, each time it opens, and gives it back
/// to where it was as it closes; a card that names none leaves the keyboard alone.</summary>
[TestFixture]
public class PopupInitialFocusTests
{
    private static void Settle(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
            window.LayoutPopups();
        }
    }

    private static (Popup Popup, TextBox Field, Button Opener, Window Window) Card(bool names)
    {
        var field = new TextBox { Width = 120 };
        var rows = new StackPanel();
        rows.Children.Add(new Button { Content = "First" });
        rows.Children.Add(field);
        var popup = new Popup { Child = rows, InitialFocus = names ? field : null };
        var opener = new Button { Content = "Open" };

        var host = new StackPanel();
        host.Children.Add(opener);
        host.Children.Add(popup);
        var window = new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = host };
        Settle(window);
        FocusManager.Focus(opener);
        return (popup, field, opener, window);
    }

    [Test]
    public void TheNamedField_TakesTheKeyboardEachTimeTheCardOpens_AndClosingGivesItBack()
    {
        var (popup, field, opener, window) = Card(true);

        popup.IsOpen = true;
        var first = FocusManager.Focused;
        Settle(window);
        popup.IsOpen = false;
        Settle(window);
        var back = FocusManager.Focused;
        popup.IsOpen = true;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.SameAs(field), "at once, before a frame has passed");
            Assert.That(back, Is.SameAs(opener));
            Assert.That(FocusManager.Focused, Is.SameAs(field), "and again on the next opening");
        });
    }

    [Test]
    public void ACardThatNamesNoField_LeavesTheKeyboardWhereItWas()
    {
        var (popup, _, opener, window) = Card(false);

        popup.IsOpen = true;
        Settle(window);

        Assert.That(FocusManager.Focused, Is.SameAs(opener));
    }

    [Test]
    public void AFieldThatCannotTakeTheKeyboardThen_IsPassedOver_AndDoesNotTakeItLater()
    {
        var (popup, field, opener, window) = Card(true);
        field.IsEnabled = false;

        popup.IsOpen = true;
        Settle(window);
        field.IsEnabled = true;
        Settle(window);

        Assert.That(FocusManager.Focused, Is.SameAs(opener));
    }

    [Test]
    public void AFieldInAHiddenPartOfTheCard_OrOutsideIt_IsPassedOver()
    {
        var (popup, field, opener, window) = Card(true);
        var outside = new TextBox();
        ((StackPanel)opener.VisualParent).Children.Add(outside);
        var rows = (StackPanel)field.VisualParent;
        rows.Children.Remove(field);
        var hidden = new StackPanel { Visibility = Visibility.Collapsed };
        hidden.Children.Add(field);
        rows.Children.Add(hidden);
        Settle(window);

        popup.IsOpen = true;
        Settle(window);
        var inHidden = FocusManager.Focused;
        popup.IsOpen = false;
        popup.InitialFocus = outside;
        popup.IsOpen = true;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(inHidden, Is.SameAs(opener));
            Assert.That(FocusManager.Focused, Is.SameAs(opener));
        });
    }

    [Test]
    public void AFieldNamedWhileTheCardIsOpen_TakesTheKeyboard()
    {
        var (popup, field, _, window) = Card(false);
        popup.IsOpen = true;
        Settle(window);

        popup.InitialFocus = field;

        Assert.That(FocusManager.Focused, Is.SameAs(field));
    }
}
