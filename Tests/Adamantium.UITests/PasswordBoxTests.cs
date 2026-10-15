using System.ComponentModel;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A PASSWORD BOX edits like a text box and shows a mask; what it holds binds like any property, and never
/// leaves by the clipboard, by undo or by automation.</summary>
[TestFixture]
public class PasswordBoxTests
{
    private const char Bullet = (char)0x25CF;
    private const char GraphemeJoiner = (char)0x034F;
    private const char CombiningAcute = (char)0x0301;

    private sealed class Shown : PasswordBox
    {
        public string For(string password) => DisplayedText(password);
    }

    private sealed class Account : INotifyPropertyChanged
    {
        private string _secret = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Secret
        {
            get => _secret;
            set
            {
                _secret = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Secret)));
            }
        }
    }

    private static string Emoji => char.ConvertFromUtf32(0x1F600);

    private static void Type(PasswordBox box, string text)
        => box.RaiseEvent(new TextInputEventArgs(text) { RoutedEvent = Keyboard.TextInputEvent });

    private static bool Press(PasswordBox box, Key key, InputModifiers modifiers = InputModifiers.None)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0) { RoutedEvent = Keyboard.KeyDownEvent };
        box.RaiseEvent(args);
        return args.Handled;
    }

    [Test]
    public void TypingGoesIntoThePassword()
    {
        var box = new PasswordBox();

        Type(box, "s3");
        Type(box, "cret");

        Assert.Multiple(() =>
        {
            Assert.That(box.Password, Is.EqualTo("s3cret"));
            Assert.That(box.CaretIndex, Is.EqualTo(6));
            Assert.That(box.HasPassword, Is.True);
        });
    }

    // What WPF never had: the password binds, both ways.
    [Test]
    public void ThePasswordBindsBothWays()
    {
        var account = new Account();
        var box = new PasswordBox { DataContext = account };
        box.SetBinding(PasswordBox.PasswordProperty, new Binding(nameof(Account.Secret)));

        Type(box, "abc");
        var typed = account.Secret;
        account.Secret = "from the model";
        BindingUpdateQueue.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(typed, Is.EqualTo("abc"), "typing reaches the model");
            Assert.That(box.Password, Is.EqualTo("from the model"), "the model reaches the box");
        });
    }

    // One mask character per character the reader sees: an emoji and a letter with its accent are one each, and the mask
    // keeps the password's length, so the caret stops where the password's characters begin.
    [Test]
    public void TheMaskIsOneCharacterPerCharacter()
    {
        var box = new Shown();
        var password = "a" + Emoji + "e" + CombiningAcute;

        var mask = box.For(password);

        Assert.Multiple(() =>
        {
            Assert.That(mask.Length, Is.EqualTo(password.Length));
            Assert.That(mask.Replace(GraphemeJoiner.ToString(), string.Empty), Is.EqualTo(new string(Bullet, 3)));
        });
    }

    [Test]
    public void TheCaretStepsOverAWholeCharacter()
    {
        var box = new PasswordBox { Password = "a" + Emoji + "b" };
        box.CaretIndex = 1;

        Press(box, Key.RightArrow);

        Assert.That(box.CaretIndex, Is.EqualTo(3), "the emoji is one step");
    }

    [Test]
    public void ItsOwnCharacterAndAShownPasswordAreHonored()
    {
        var box = new Shown { PasswordChar = '*' };
        var masked = box.For("abc");

        box.IsPasswordRevealed = true;

        Assert.Multiple(() =>
        {
            Assert.That(masked, Is.EqualTo("***"));
            Assert.That(box.For("abc"), Is.EqualTo("abc"), "revealed, the password is shown as it is");
        });
    }

    // The clipboard never carries it out: copy and cut do nothing, paste still works.
    [Test]
    public void CopyAndCutDoNothing_PasteWorks()
    {
        var box = new PasswordBox { Password = "secret" };
        box.SelectAll();
        Clipboard.SetText("untouched");

        Press(box, Key.C, InputModifiers.LeftControl);
        Press(box, Key.X, InputModifiers.LeftControl);
        var afterCopyAndCut = (Clipboard.GetText(), box.Password);

        Clipboard.SetText("pasted");
        box.SelectAll();
        Press(box, Key.V, InputModifiers.LeftControl);

        Assert.Multiple(() =>
        {
            Assert.That(afterCopyAndCut, Is.EqualTo(("untouched", "secret")));
            Assert.That(box.Password, Is.EqualTo("pasted"));
        });
    }

    // No history: undo would keep the passwords the box once held.
    [Test]
    public void UndoBringsNothingBack()
    {
        var box = new PasswordBox();
        Type(box, "first");
        box.SelectAll();
        Type(box, "second");

        Press(box, Key.Z, InputModifiers.LeftControl);

        Assert.That(box.Password, Is.EqualTo("second"));
    }

    // Words would tell where the password has its spaces: Ctrl+arrows go to the ends, a double-click takes it all.
    [Test]
    public void ThereAreNoWordsInAPassword()
    {
        var box = new PasswordBox { Password = "two words" };
        box.CaretIndex = 5;

        Press(box, Key.LeftArrow, InputModifiers.LeftControl);
        var left = box.CaretIndex;
        box.CaretIndex = 1;
        Press(box, Key.RightArrow, InputModifiers.LeftControl);
        var right = box.CaretIndex;
        box.SelectWordAt(2);

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.Zero);
            Assert.That(right, Is.EqualTo(9));
            Assert.That((box.SelectionStart, box.SelectionLength), Is.EqualTo((0, 9)));
        });
    }

    // Automation is told it is a password and never what it is; a test can still type one in.
    [Test]
    public void AutomationSeesAPasswordFieldWithoutItsValue()
    {
        var box = new PasswordBox { Password = "secret", Placeholder = "Password" };
        var peer = (PasswordBoxAutomationPeer)box.GetAutomationPeer();

        peer.SetValue("typed by a test");

        Assert.Multiple(() =>
        {
            Assert.That(peer.IsPassword, Is.True);
            Assert.That(peer.ControlType, Is.EqualTo(AutomationControlType.Edit));
            Assert.That(peer.Value, Is.Empty);
            Assert.That(peer.GetPattern(PatternId.Text), Is.Null, "no text pattern to read it by");
            Assert.That(peer.Name, Is.EqualTo("Password"));
            Assert.That(box.Password, Is.EqualTo("typed by a test"));
        });
    }

    // Hidden takes the button away and leaves showing to the application - a "show password" check box of its own.
    [Test]
    public void WithoutTheButtonTheApplicationStillDecides()
    {
        var box = new Shown { PasswordRevealMode = PasswordRevealMode.Hidden };

        box.IsPasswordRevealed = true;

        Assert.That(box.For("abc"), Is.EqualTo("abc"));
    }

    // A password copied with the line break after it does not gain a character nobody sees.
    [Test]
    public void APasteTakesTheFirstLineWithoutControlCharacters()
    {
        var box = new PasswordBox();
        Clipboard.SetText("se\tcret\r\nsecond line");

        Press(box, Key.V, InputModifiers.LeftControl);

        Assert.That(box.Password, Is.EqualTo("secret"));
    }

    private static (PasswordBox Box, Adamantium.UI.Controls.Decorators.Border Around, Adamantium.UI.Controls.Window Window) InAWindow()
    {
        var box = new PasswordBox();
        var around = new Adamantium.UI.Controls.Decorators.Border { Child = box };
        var window = new Adamantium.UI.Controls.Window { Width = 400, Height = 200, Content = around };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        return (box, around, window);
    }

    // The input method would show the password in clear: it is off while the box has the keyboard, on again after.
    [Test]
    public void TheInputMethodIsOffWhileTheBoxHasTheKeyboard()
    {
        var (box, _, window) = InAWindow();

        FocusManager.Focus(box);
        var whileFocused = window.IsInputMethodOff;
        FocusManager.Release(box);

        Assert.Multiple(() =>
        {
            Assert.That(whileFocused, Is.True);
            Assert.That(window.IsInputMethodOff, Is.False);
        });
    }

    // Taken out of the window with the keyboard - a login view replaced, a dialog closed - it turns the input method back
    // on in the window it turned it off in, or nobody could type Japanese there again.
    [Test]
    public void TakenOutWithTheKeyboardItTurnsTheInputMethodBackOn()
    {
        var (box, around, window) = InAWindow();
        FocusManager.Focus(box);
        Assume.That(window.IsInputMethodOff, Is.True);

        around.Child = null;

        Assert.That(window.IsInputMethodOff, Is.False);
    }

    [Test]
    public void ThePlatformsOwnClipboardKeysWork()
    {
        var box = new PasswordBox();
        Clipboard.SetText("pasted");

        Press(box, Key.Insert, InputModifiers.LeftShift);

        Assert.That(box.Password, Is.EqualTo("pasted"));
    }

    // AltGr arrives as Ctrl+Alt: AltGr+A types a letter (Polish a-ogonek) and must not select everything it would replace.
    [Test]
    public void AltGrIsNoShortcut()
    {
        var box = new PasswordBox { Password = "secret" };
        box.CaretIndex = 6;

        var handled = Press(box, Key.A, InputModifiers.LeftControl | InputModifiers.RightAlt);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.False);
            Assert.That(box.SelectionLength, Is.Zero);
        });
    }
}
