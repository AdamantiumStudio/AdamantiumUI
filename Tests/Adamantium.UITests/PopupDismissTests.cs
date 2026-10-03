using System.ComponentModel;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A popup the person puts away - a press outside it - tells the application's binding on <c>IsOpen</c>, and
/// the binding still holds afterwards: the view model opens it again.</summary>
[TestFixture]
public class PopupDismissTests
{
    private sealed class Shown : INotifyPropertyChanged
    {
        private bool _isOpen;

        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                _isOpen = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOpen)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static void Press(IUIComponent target, Window window)
    {
        ((IObservableComponent)target).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, MouseButtons.Left,
            MouseButtonState.Pressed, InputModifiers.LeftMouseButton, 0)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        });

        Settle(window);
    }

    [Test]
    public void APopupPutAwayByAPressOutside_TellsItsBinding_AndOpensAgainFromIt()
    {
        var shown = new Shown { IsOpen = true };
        var outside = new Border { Width = 40, Height = 40 };
        var popup = new Popup
        {
            KeepOpen = false,
            DataContext = shown,
            Child = new Border { Width = 60, Height = 40 }
        };
        popup.SetBinding(nameof(Popup.IsOpen), new Binding(nameof(Shown.IsOpen))
        {
            Mode = BindingMode.TwoWay,
            IsImmediate = true
        });

        var host = new StackPanel();
        host.Children.Add(outside);
        host.Children.Add(popup);
        var window = new Window { Width = 400, Height = 300, Content = host };
        Settle(window);
        Assert.That(window.PopupLayer.HasPopups, Is.True, "precondition: the popup is up");

        Press(outside, window);
        Assert.That(shown.IsOpen, Is.False, "the press put it away, and the view model heard it");

        shown.IsOpen = true;
        Settle(window);
        Assert.That(window.PopupLayer.HasPopups, Is.True, "the binding still holds: the view model opens it again");
    }

    // A menu with a card opened from one of its rows, and another from the next row.
    private static (Popup Menu, Popup First, Popup Second, Border Row, Window Window) MenuWithTwoCards()
    {
        var first = new Popup { KeepOpen = false, Child = new Border { Width = 80, Height = 40 } };
        var second = new Popup { KeepOpen = false, Child = new Border { Width = 80, Height = 40 } };
        var row = new Border { Width = 100, Height = 20 };
        var rows = new StackPanel();
        rows.Children.Add(row);
        rows.Children.Add(first);
        rows.Children.Add(second);
        var menu = new Popup { KeepOpen = false, Child = new Border { Child = rows } };

        var host = new StackPanel();
        host.Children.Add(menu);
        var window = new Window { Width = 400, Height = 300, Content = host };
        Settle(window);
        menu.IsOpen = true;
        window.PopupLayer.UpdateLayout(new Size(400, 300));
        Settle(window);
        return (menu, first, second, row, window);
    }

    // What the mouse does: the press is raised on what was hit, and its route ends at the root of the overlay it is in.
    private static void PressOn(IUIComponent pressed, Window window)
    {
        ((IObservableComponent)pressed).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, MouseButtons.Left,
            MouseButtonState.Pressed, InputModifiers.LeftMouseButton, 0)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        });

        Settle(window);
    }

    [Test]
    public void APressElsewhereInTheMenu_PutsAwayACardOpenedFromIt_AndKeepsTheMenu()
    {
        var (menu, first, _, row, window) = MenuWithTwoCards();
        first.IsOpen = true;
        Settle(window);

        PressOn(row, window);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsOpen, Is.False, "a press on the menu is outside the card");
            Assert.That(menu.IsOpen, Is.True, "the menu itself was pressed - it stays");
        });
    }

    [Test]
    public void OpeningTheNextCard_PutsAwayTheFirst()
    {
        var (menu, first, second, _, window) = MenuWithTwoCards();
        first.IsOpen = true;
        second.IsOpen = true;
        Settle(window);

        PressOn((IUIComponent)second.Child, window);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsOpen, Is.False, "the cards are siblings: one is outside the other");
            Assert.That(second.IsOpen, Is.True, "the press was inside this one");
            Assert.That(menu.IsOpen, Is.True, "a card opened from the menu is inside it");
        });
    }
}
