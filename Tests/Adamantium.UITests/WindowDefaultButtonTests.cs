using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Enter in a window's single-line field presses its default button, as in WPF - the one that is shown and can
/// be pressed - and only a plain Enter does; a field that takes lines keeps the key.</summary>
[TestFixture]
public class WindowDefaultButtonTests
{
    private static (TextBox Box, Button Shown, Button Hidden, Button Disabled, Window Window) Form()
    {
        var hidden = new Button { Content = "Hidden", IsDefault = true };
        var hiding = new StackPanel { Visibility = Visibility.Collapsed };
        hiding.Children.Add(hidden);
        var disabled = new Button { Content = "Disabled", IsDefault = true };
        var off = new StackPanel { IsEnabled = false };
        off.Children.Add(disabled);
        var shown = new Button { Content = "OK", IsDefault = true };
        var box = new TextBox();

        var rows = new StackPanel();
        rows.Children.Add(box);
        rows.Children.Add(hiding);
        rows.Children.Add(off);
        rows.Children.Add(shown);
        var window = new Window { Width = 300, Height = 200, Content = rows };
        WindowExtension.UpdateTree(window);
        return (box, shown, hidden, disabled, window);
    }

    private static KeyEventArgs Press(Key key, object on, InputModifiers modifiers = InputModifiers.None) =>
        new(KeyboardDevice.CurrentDevice, key, modifiers, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            OriginalSource = on
        };

    [Test]
    public void EnterInAField_PressesTheDefaultButtonThatIsShown_NotOneHiddenOrOff()
    {
        var (box, shown, hidden, disabled, _) = Form();
        System.Collections.Generic.List<string> clicks = [];
        shown.Click += (_, _) => clicks.Add("OK");
        hidden.Click += (_, _) => clicks.Add("Hidden");
        disabled.Click += (_, _) => clicks.Add("Disabled");

        var enter = Press(Key.Enter, box);
        box.RaiseEvent(enter);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(new[] { "OK" }));
            Assert.That(enter.Handled, Is.True);
        });
    }

    [Test]
    public void AChordedEnter_OrEnterInALinesField_PressesNothing()
    {
        var (box, shown, _, _, _) = Form();
        var clicks = 0;
        shown.Click += (_, _) => clicks++;

        box.RaiseEvent(Press(Key.Enter, box, InputModifiers.LeftShift));
        box.RaiseEvent(Press(Key.Enter, box, InputModifiers.LeftControl));
        box.AcceptsReturn = true;
        box.RaiseEvent(Press(Key.Enter, box));

        Assert.That(clicks, Is.Zero);
    }
}
