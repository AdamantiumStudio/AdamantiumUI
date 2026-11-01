using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>An overlay gives the focus back to whatever had it when it opened - unless that is gone from its window
/// since. A slider torn down mid-drag, its value badge closed with it, took the focus back and held its page through the
/// focus manager.</summary>
[TestFixture]
public class FocusReturnTests
{
    [TearDown]
    public void LetGoOfTheFocus() => FocusManager.ResetFocus();

    [Test]
    public void AnElementGoneFromItsWindow_IsNotGivenTheFocusBack()
    {
        var opener = new Button();
        var panel = new StackPanel();
        panel.Children.Add(opener);
        var window = new Window { Width = 200, Height = 100, Content = panel };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        FocusManager.Focus(opener);
        Assume.That(FocusManager.Focused, Is.SameAs(opener));
        var overlay = new Border();
        var focusReturn = new FocusReturn();
        focusReturn.Capture();

        panel.Children.Remove(opener);
        focusReturn.Restore(overlay);

        Assert.That(FocusManager.Focused, Is.Not.SameAs(opener));
    }

    // A card opened from a menu's item: the item is gone with its menu by the time the card closes, and the focus goes
    // to the field the menu was opened from - it had the focus last in the scope the card stands in.
    [Test]
    public void AnOpenerGone_GivesTheFocusToWhatHadItLastAroundTheOverlay()
    {
        var field = new Button();
        var item = new Button();
        var menu = new StackPanel();
        FocusManager.SetIsFocusScope(menu, true);
        menu.Children.Add(item);
        var panel = new StackPanel();
        panel.Children.Add(field);
        panel.Children.Add(menu);
        var window = new Window { Width = 200, Height = 100, Content = panel };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        FocusManager.Focus(field);
        FocusManager.Focus(item);
        Assume.That(FocusManager.Focused, Is.SameAs(item));
        var focusReturn = new FocusReturn();
        focusReturn.Capture();
        var overlay = new Border();

        panel.Children.Remove(menu);
        FocusManager.ResetFocus();
        focusReturn.Restore(overlay, panel);

        Assert.That(FocusManager.Focused, Is.SameAs(field));
    }

    [Test]
    public void AnElementStillInItsWindow_IsGivenTheFocusBack()
    {
        var opener = new Button();
        var panel = new StackPanel();
        panel.Children.Add(opener);
        var window = new Window { Width = 200, Height = 100, Content = panel };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        FocusManager.Focus(opener);
        var focusReturn = new FocusReturn();
        focusReturn.Capture();
        FocusManager.ResetFocus();
        Assume.That(FocusManager.Focused, Is.Null, "the focus is let go of");

        focusReturn.Restore(new Border());

        Assert.That(FocusManager.Focused, Is.SameAs(opener));
    }
}
