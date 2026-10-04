using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A row that is a switch: choosing it flips its check, and a list of such switches may stay open while
/// several are flipped - a menu of what a document is made of.</summary>
[TestFixture]
public class MenuItemCheckTests
{
    private static KeyEventArgs Press(Key key) =>
        new(KeyboardDevice.CurrentDevice, key, InputModifiers.None, 0) { RoutedEvent = Keyboard.KeyDownEvent };

    private static ControlTemplate CardTemplate() => new(() =>
    {
        var popup = new Popup
        {
            ChildTemplate = new ControlTemplate(() =>
            {
                var presenter = new ItemsPresenter();
                var scroll = new MenuScrollViewer { Content = presenter };
                var inner = new TemplateResult { RootComponent = new Border { Child = scroll } };
                inner.RegisterName("PART_MenuScroll", scroll);
                inner.RegisterName("PART_ItemsPresenter", presenter);
                return inner;
            })
        };

        var grid = new Grid();
        grid.Children.Add(popup);

        var result = new TemplateResult { RootComponent = grid };
        result.RegisterName("PART_Popup", popup);
        return result;
    });

    private static (ContextMenu Menu, IInputComponent Rows) OpenMenuWith(MenuItem row)
    {
        var menu = new ContextMenu { Template = CardTemplate() };
        menu.Items.Add(row);
        var window = new Window { Width = 400, Height = 300, Content = menu };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        menu.IsOpen = true;
        for (var i = 0; i < 3; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        var popup = menu.GetTemplateChild("PART_Popup") as Popup;
        return (menu, (IInputComponent)popup.FindContentChild("PART_ItemsPresenter"));
    }

    // What a chosen row sends up to the rows' host, where the menu listens.
    private static void Chosen(IInputComponent rows, MenuItem row) =>
        rows.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, row) { RoutedEvent = MenuItem.ClickEvent });

    [Test]
    public void ChoosingACheckableRow_FlipsItsCheck()
    {
        var row = new MenuItem { Header = "Surface", IsCheckable = true };

        row.RaiseEvent(Press(Key.Enter));
        Assert.That(row.IsChecked, Is.True);

        row.RaiseEvent(Press(Key.Enter));
        Assert.That(row.IsChecked, Is.False, "and back");
    }

    [Test]
    public void ARowThatIsNotCheckable_LeavesItsCheckAlone()
    {
        var row = new MenuItem { Header = "Grid", IsChecked = true };

        row.RaiseEvent(Press(Key.Enter));

        Assert.That(row.IsChecked, Is.True, "a plain command shows a check it was given, it does not flip it");
    }

    [Test]
    public void ACheckableRowStillRunsItsCommand()
    {
        var command = new SwitchableCommand { CanRun = true };
        var row = new MenuItem { Header = "Surface", IsCheckable = true, Command = command };

        row.RaiseEvent(Press(Key.Enter));

        Assert.That(command.Runs, Is.EqualTo(1));
    }

    [Test]
    public void ARowThatStaysOpen_LeavesTheMenuOpen()
    {
        var row = new MenuItem { Header = "Surface", IsCheckable = true, StaysOpenOnClick = true };
        var (menu, rows) = OpenMenuWith(row);
        Assert.That(menu.IsOpen, Is.True, "precondition: the menu is up");

        Chosen(rows, row);

        Assert.That(menu.IsOpen, Is.True, "the next switch is one click away, so the list stays");
    }

    [Test]
    public void AnOrdinaryRow_StillClosesTheMenu()
    {
        var row = new MenuItem { Header = "Attach a module" };
        var (menu, rows) = OpenMenuWith(row);

        Chosen(rows, row);

        Assert.That(menu.IsOpen, Is.False);
    }
}
