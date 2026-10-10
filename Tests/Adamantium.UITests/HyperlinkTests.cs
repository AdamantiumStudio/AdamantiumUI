using System;
using System.Linq;
using Adamantium.Core.Commands;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Links inside a TextBlock: activated they raise Click, run their command and ask to navigate; a block with
/// links takes the keyboard, Tab walks its links and Enter activates one; automation sees each link.</summary>
[TestFixture]
public class HyperlinkTests
{
    private sealed class CountingCommand : ICommand
    {
        public int Runs { get; private set; }

        public object Parameter { get; private set; }

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter) => true;

        public void RaiseCanExecuteChanged()
        {
        }

        public void Execute(object parameter)
        {
            Runs++;
            Parameter = parameter;
        }
    }

    private static (TextBlock Text, Hyperlink First, Hyperlink Second) Block()
    {
        var text = new TextBlock();
        var first = new Hyperlink();
        first.Inlines.Add(new Run { Text = "first" });
        var second = new Hyperlink { NavigateUri = new Uri("https://example.com/") };
        second.Inlines.Add(new Run { Text = "second" });
        text.Inlines.Add(new Run { Text = "Go to " });
        text.Inlines.Add(first);
        text.Inlines.Add(new Run { Text = " or " });
        text.Inlines.Add(second);
        var window = new Window { Width = 600, Height = 300, Content = new Border { Child = text } };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return (text, first, second);
    }

    private static void Press(TextBlock text, Key key) =>
        ((IObservableComponent)text).RaiseEvent(new KeyEventArgs(KeyboardDevice.CurrentDevice, key, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });

    [Test]
    public void Activated_ItClicks_RunsItsCommand_AndAsksToNavigate()
    {
        var command = new CountingCommand();
        var link = new Hyperlink { Command = command, CommandParameter = 7, NavigateUri = new Uri("https://example.com/") };
        var clicks = 0;
        Uri asked = null;
        link.Click += (_, _) => clicks++;
        link.RequestNavigate += (_, e) =>
        {
            asked = e.Uri;
            e.Handled = true;
        };

        link.Activate();

        Assert.That(clicks, Is.EqualTo(1));
        Assert.That(command.Runs, Is.EqualTo(1));
        Assert.That(command.Parameter, Is.EqualTo(7));
        Assert.That(asked, Is.EqualTo(new Uri("https://example.com/")));
    }

    [Test]
    public void ALink_IsUnderlinedByDefault()
    {
        var (text, _, _) = Block();

        Assert.That(text.Layout.AttributedText.AttributesAt(6).Decorations,
            Is.EqualTo(Adamantium.Graphics.Fonts.TextDecorations.Underline));
    }

    [Test]
    public void ABlockWithLinks_TakesTheKeyboard_ABlockWithoutDoesNot()
    {
        var (text, _, _) = Block();
        var plain = new TextBlock { Text = "plain" };

        Assert.That(text.Focusable, Is.True);
        Assert.That(plain.Focusable, Is.False);
    }

    [Test]
    public void Tab_WalksTheLinks_EnterActivatesTheOneWithTheKeyboard()
    {
        var (text, first, second) = Block();
        var firstClicks = 0;
        var secondClicks = 0;
        first.Click += (_, _) => firstClicks++;
        second.Click += (_, _) => secondClicks++;
        second.RequestNavigate += (_, e) => e.Handled = true;
        text.Focus();

        Press(text, Key.Tab);
        Press(text, Key.Enter);

        Assert.That(firstClicks, Is.EqualTo(0));
        Assert.That(secondClicks, Is.EqualTo(1));
    }

    [Test]
    public void ABlockTheAuthorKeptOutOfTheKeyboard_StaysOut()
    {
        var text = new TextBlock { Focusable = false };
        var link = new Hyperlink();
        link.Inlines.Add(new Run { Text = "link" });
        text.Inlines.Add(link);
        var window = new Window { Width = 600, Height = 300, Content = text };
        WindowExtension.UpdateTree(window);

        Assert.That(text.Focusable, Is.False);
    }

    [Test]
    public void ThePressOnPlainText_LeavesTheFocusToTheButtonAround()
    {
        var (text, _, _) = Block();
        var button = new Adamantium.UI.Controls.Buttons.Button();
        ((Border)text.VisualParent).Child = null;
        button.Content = text;
        var window = new Window { Width = 600, Height = 300, Content = button };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        ((IObservableComponent)text).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, MouseButtons.Left,
            MouseButtonState.Pressed, InputModifiers.LeftMouseButton, 0) { RoutedEvent = Mouse.PreviewMouseDownEvent });

        Assert.That(text.IsKeyboardFocused, Is.False);
        Assert.That(button.IsKeyboardFocused, Is.True);
    }

    [Test]
    public void TheFocusRing_GoesRoundTheLinkWithTheKeyboard()
    {
        var (text, _, _) = Block();
        text.Focus();
        var first = text.FocusBounds;

        Press(text, Key.Tab);

        Assert.That(first, Is.Not.Null);
        Assert.That(text.FocusBounds, Is.Not.Null);
        Assert.That(text.FocusBounds.Value.X, Is.GreaterThan(first.Value.X + first.Value.Width), "the second link is right of the first");
    }

    [Test]
    public void Automation_SeesEachLink_NamedByItsText_AndInvokesIt()
    {
        var (text, first, _) = Block();
        var clicks = 0;
        first.Click += (_, _) => clicks++;

        var links = text.GetAutomationPeer().GetChildren().OfType<HyperlinkAutomationPeer>().ToList();
        links[0].Invoke();

        Assert.That(links.Select(peer => peer.Name), Is.EqualTo(new[] { "first", "second" }));
        Assert.That(links[0].ControlType, Is.EqualTo(AutomationControlType.Hyperlink));
        Assert.That(clicks, Is.EqualTo(1));
    }
}
