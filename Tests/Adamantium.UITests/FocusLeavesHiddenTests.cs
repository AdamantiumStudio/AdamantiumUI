using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A focused element that stops being shown hands the focus to the nearest ancestor that can hold it. A menu
/// laid over a game's surface closed with its Continue button focused, and every key went on into the hidden button
/// instead of the game.</summary>
[TestFixture]
public class FocusLeavesHiddenTests
{
    [TearDown]
    public void LetGoOfTheFocus() => FocusManager.ResetFocus();

    [TestCase(Visibility.Collapsed)]
    [TestCase(Visibility.Hidden)]
    public void HidingTheFocusedElementsContainer_MovesTheFocusToTheNearestFocusableAncestor(Visibility hidden)
    {
        var (surface, menu, button) = SurfaceWithMenu();
        FocusManager.Focus(button);
        Assume.That(FocusManager.Focused, Is.SameAs(button));

        menu.Visibility = hidden;

        Assert.That(FocusManager.Focused, Is.SameAs(surface));
    }

    [Test]
    public void AFocusTheKeyboardBrought_KeepsItsRingWhenItMoves()
    {
        var (surface, menu, button) = SurfaceWithMenu();
        FocusManager.Focus(button, NavigationMethod.Tab);

        menu.Visibility = Visibility.Collapsed;

        Assert.Multiple(() =>
        {
            Assert.That(FocusManager.Focused, Is.SameAs(surface));
            Assert.That(FocusManager.IsFocusVisible, Is.True);
        });
    }

    [Test]
    public void HidingAnotherElement_LeavesTheFocusWhereItIs()
    {
        var other = new Button();
        var (_, _, button) = SurfaceWithMenu(other);
        FocusManager.Focus(button);

        other.Visibility = Visibility.Collapsed;

        Assert.That(FocusManager.Focused, Is.SameAs(button));
    }

    [Test]
    public void WithNoAncestorToTakeIt_TheFocusIsLetGoOf()
    {
        var button = new Button();
        var menu = new Border { Child = button };
        var window = new Window { Width = 200, Height = 100, Content = menu };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        FocusManager.Focus(button);

        menu.Visibility = Visibility.Collapsed;

        Assert.Multiple(() =>
        {
            Assert.That(FocusManager.Focused, Is.Null);
            Assert.That(KeyboardDevice.CurrentDevice.FocusedComponent, Is.Null, "no key goes into the hidden button");
        });
    }

    [Test]
    public void AWindowComingBack_DoesNotReturnTheFocusIntoAHiddenElement()
    {
        var button = new Button();
        var menu = new Border { Child = button };
        var window = new Window { Width = 200, Height = 100, Content = menu };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        FocusManager.Focus(button);
        menu.Visibility = Visibility.Collapsed;

        Assert.Multiple(() =>
        {
            Assert.That(FocusManager.TryRestoreFocus(window), Is.False);
            Assert.That(FocusManager.Focused, Is.Null);
        });
    }

    private static (RenderTargetPanel Surface, Border Menu, Button Button) SurfaceWithMenu(Button sibling = null)
    {
        var button = new Button();
        var items = new StackPanel();
        items.Children.Add(button);
        if (sibling != null)
        {
            items.Children.Add(sibling);
        }
        var menu = new Border { Child = items };
        var surface = new RenderTargetPanel();
        surface.Children.Add(menu);
        var window = new Window { Width = 200, Height = 100, Content = surface };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        return (surface, menu, button);
    }
}
