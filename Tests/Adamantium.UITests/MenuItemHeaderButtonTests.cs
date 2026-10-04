using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A button a menu row holds - an action beside the row's own, such as taking a module out of the document - is
/// pressed for itself: the release reaches the row too, and the row must not be chosen by it.</summary>
[TestFixture]
public class MenuItemHeaderButtonTests
{
    private static (MenuItem Row, Button Action, Border Body) RowWithAButton()
    {
        var action = new Button { Width = 20, Height = 20 };
        var body = new Border { Width = 100, Height = 20 };
        var row = new MenuItem
        {
            IsCheckable = true,
            Template = new ControlTemplate(() =>
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(body);
                panel.Children.Add(action);
                return new TemplateResult { RootComponent = panel };
            })
        };

        var window = new Window { Width = 400, Height = 300, Content = row };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return (row, action, body);
    }

    private static void ReleaseOver(MenuItem row, IUIComponent over) =>
        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, MouseButtons.Left, MouseButtonState.Released,
            InputModifiers.None, 0)
        {
            RoutedEvent = InputUIComponent.MouseLeftButtonUpEvent,
            OriginalSource = over
        });

    [Test]
    public void ReleasingOnAButtonInTheRow_LeavesTheRowAlone()
    {
        var (row, action, _) = RowWithAButton();
        var clicked = 0;
        row.Click += (_, _) => clicked++;

        ReleaseOver(row, action);

        Assert.Multiple(() =>
        {
            Assert.That(row.IsChecked, Is.False, "the button ran itself; the row's switch stays where it was");
            Assert.That(clicked, Is.Zero, "and the row was not chosen - its menu would have closed");
        });
    }

    [Test]
    public void ReleasingOnTheRowItself_StillChoosesIt()
    {
        var (row, _, body) = RowWithAButton();

        ReleaseOver(row, body);

        Assert.That(row.IsChecked, Is.True);
    }
}
