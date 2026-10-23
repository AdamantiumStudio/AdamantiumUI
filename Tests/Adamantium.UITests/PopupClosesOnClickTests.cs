using Adamantium.Core.Commands;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A card on a popup answers as a dialog does: its "Go" closes it once its command has run, and Enter in it
/// presses its default button - though its keys never reach the window.</summary>
[TestFixture]
public class PopupClosesOnClickTests
{
    private sealed class Counted : ICommand
    {
        public System.Func<object> Read { get; set; }

        public object Seen { get; private set; }

        public int Runs { get; private set; }

        public event System.EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            Seen = Read?.Invoke();
            Runs++;
        }

        public void RaiseCanExecuteChanged()
        {
        }
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static (Popup Popup, Button Go, Button Other, NumericUpDown Field, Window Window) Card(Counted go)
    {
        var goButton = new Button { Content = "Go", Command = go, IsDefault = true };
        Popup.SetClosesOnClick(goButton, true);
        var other = new Button { Content = "More" };
        var field = new NumericUpDown { Width = 80, Template = NumberTemplate() };
        var rows = new StackPanel();
        rows.Children.Add(field);
        rows.Children.Add(other);
        rows.Children.Add(goButton);
        var popup = new Popup { Child = rows };

        var host = new StackPanel();
        host.Children.Add(popup);
        var window = new Window { Width = 400, Height = 300, Content = host };
        Settle(window);
        popup.IsOpen = true;
        Settle(window);
        return (popup, goButton, other, field, window);
    }

    private static KeyEventArgs Press(Key key, object on) =>
        new(KeyboardDevice.CurrentDevice, key, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            OriginalSource = on
        };

    [Test]
    public void AButtonThatClosesOnClick_RunsItsCommand_ThenClosesThePopup()
    {
        var go = new Counted();
        var (popup, button, _, _, window) = Card(go);

        button.PerformClick();
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(go.Runs, Is.EqualTo(1));
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(window.PopupLayer.HasPopups, Is.False);
        });
    }

    [Test]
    public void AnotherButton_LeavesThePopupOpen()
    {
        var (popup, _, other, _, window) = Card(new Counted());

        other.PerformClick();
        Settle(window);

        Assert.That(popup.IsOpen, Is.True);
    }

    [Test]
    public void AnEnterNothingTook_PressesTheDefaultButton()
    {
        var go = new Counted();
        var (popup, _, _, field, window) = Card(go);
        go.Read = () => field.Value;
        field.Value = 7;
        var rows = (StackPanel)field.VisualParent;

        rows.RaiseEvent(Press(Key.Enter, rows));
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(go.Runs, Is.EqualTo(1));
            Assert.That(go.Seen, Is.EqualTo(7.0));
            Assert.That(popup.IsOpen, Is.False, "and the default button closes it");
        });
    }

    [Test]
    public void EnterInAField_IsTheFields_AsInAWindow()
    {
        var go = new Counted();
        var (popup, _, _, field, window) = Card(go);
        var box = TextBoxIn(field);
        box.Text = "7";

        box.RaiseEvent(Press(Key.Enter, box));
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(field.Value, Is.EqualTo(7.0), "the field took its number in");
            Assert.That(go.Runs, Is.Zero);
            Assert.That(popup.IsOpen, Is.True);
        });
    }

    [Test]
    public void EnterOnAnotherButton_OrInALinesField_IsTheirsAlone()
    {
        var go = new Counted();
        var (popup, _, other, field, window) = Card(go);
        var lines = new TextBox { AcceptsReturn = true };
        ((StackPanel)field.VisualParent).Children.Add(lines);
        Settle(window);

        other.RaiseEvent(Press(Key.Enter, other));
        lines.RaiseEvent(Press(Key.Enter, lines));
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(go.Runs, Is.Zero);
            Assert.That(popup.IsOpen, Is.True);
        });
    }

    [Test]
    public void AHiddenOrDisabledDefaultButton_IsNotPressed_AndEnterIsLeftAlone()
    {
        var go = new Counted();
        var (popup, button, _, field, window) = Card(go);
        var rows = (StackPanel)field.VisualParent;
        rows.Children.Insert(0, new Button { IsDefault = true, Visibility = Visibility.Collapsed, Command = go });
        var off = new StackPanel { IsEnabled = false };
        off.Children.Add(new Button { IsDefault = true, Command = go });
        rows.Children.Insert(1, off);
        button.IsEnabled = false;
        Settle(window);
        var enter = Press(Key.Enter, rows);

        rows.RaiseEvent(enter);
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(go.Runs, Is.Zero);
            Assert.That(enter.Handled, Is.False);
            Assert.That(popup.IsOpen, Is.True);
        });
    }

    private static ControlTemplate NumberTemplate() => new(() =>
    {
        var text = new TextBox();
        var result = new TemplateResult { RootComponent = text };
        result.RegisterName("PART_TextBox", text);
        return result;
    });

    private static TextBox TextBoxIn(NumericUpDown field) => (TextBox)field.GetTemplateChild("PART_TextBox");

    [Test]
    public void OpenedAgain_ItsButtonClosesItAgain()
    {
        var (popup, button, _, _, window) = Card(new Counted());
        popup.IsOpen = false;
        Settle(window);
        popup.IsOpen = true;
        Settle(window);

        button.PerformClick();
        Settle(window);

        Assert.That(popup.IsOpen, Is.False, "opened again, it is hooked again");
    }
}
