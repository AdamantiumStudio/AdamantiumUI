using System;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A key binding set anywhere in a window runs from anywhere in it: the elements around the focus have the key
/// first, then the binding nearest to the focus, as long as its element is shown and enabled.</summary>
[TestFixture]
public class WindowKeyBindingTests
{
    [TearDown]
    public void LetGoOfTheFocus() => FocusManager.ResetFocus();

    [Test]
    public void ABindingOffTheWayFromTheFocus_RunsFromAnywhereInTheWindow()
    {
        var (window, field, side) = Layout();
        var ran = 0;
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => ran++), KeyGesture.Parse("Escape")));
        Settle(window);
        FocusManager.Focus(field);

        var args = Press(field, Key.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.EqualTo(1));
            Assert.That(args.Handled, Is.True);
            Assert.That(FocusManager.Focused, Is.SameAs(field), "a key a binding took does not clear the focus");
        });
    }

    [Test]
    public void TheElementsAroundTheFocus_HaveTheKeyFirst()
    {
        var (window, field, side) = Layout();
        var around = 0;
        var elsewhere = 0;
        ((StackPanel)field.VisualParent).InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => around++),
            KeyGesture.Parse("Ctrl+F")));
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => elsewhere++), KeyGesture.Parse("Ctrl+F")));
        Settle(window);
        FocusManager.Focus(field);

        Press(field, Key.F, InputModifiers.LeftControl);

        Assert.That((around, elsewhere), Is.EqualTo((1, 0)));
    }

    [Test]
    public void OfTwoBindingsElsewhere_TheNearestToTheFocusRuns()
    {
        var field = new Button();
        var near = new StackPanel();
        var left = new StackPanel { Children = { field, near } };
        var far = new StackPanel();
        var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { left, far } } };
        var ran = new System.Collections.Generic.List<string>();
        far.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => ran.Add("far")), KeyGesture.Parse("F3")));
        near.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => ran.Add("near")), KeyGesture.Parse("F3")));
        Settle(window);
        FocusManager.Focus(field);

        Press(field, Key.F3);

        Assert.That(ran, Is.EqualTo(new[] { "near" }));
    }

    [Test]
    public void ABindingOfAHiddenOrDisabledElement_DoesNotRun_NorOneThatCannot()
    {
        var (window, field, side) = Layout();
        var ran = 0;
        var can = true;
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => ran++, () => can), KeyGesture.Parse("F2")));
        Settle(window);
        FocusManager.Focus(field);

        side.Visibility = Visibility.Collapsed;
        var hidden = Press(field, Key.F2).Handled;
        side.Visibility = Visibility.Visible;
        side.IsEnabled = false;
        var disabled = Press(field, Key.F2).Handled;
        side.IsEnabled = true;
        can = false;
        var refused = Press(field, Key.F2).Handled;
        can = true;
        var runs = Press(field, Key.F2).Handled;

        Assert.Multiple(() =>
        {
            Assert.That((hidden, disabled, refused), Is.EqualTo((false, false, false)));
            Assert.That(runs, Is.True);
            Assert.That(ran, Is.EqualTo(1));
        });
    }

    [Test]
    public void ABindingTakenOutOfTheWindow_RunsNoMore()
    {
        var (window, field, side) = Layout();
        var ran = 0;
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => ran++), KeyGesture.Parse("F4")));
        Settle(window);
        FocusManager.Focus(field);

        ((StackPanel)side.VisualParent).Children.Remove(side);
        Settle(window);
        Press(field, Key.F4);

        Assert.That(ran, Is.Zero);
    }

    [Test]
    public void ABindingsCommand_IsBoundToItsElementsData()
    {
        var (window, field, side) = Layout();
        var data = new Commands();
        side.DataContext = data;
        var binding = new KeyBinding { Gesture = KeyGesture.Parse("Escape") };
        binding.SetBinding(KeyBinding.CommandProperty, new Binding(nameof(Commands.Cancel)));
        side.InputBindings.Add(binding);
        Settle(window);
        FocusManager.Focus(field);

        Press(field, Key.Escape);

        Assert.That(data.Cancelled, Is.EqualTo(1));
    }

    [Test]
    public void WithNothingFocused_AKeyStillReachesTheBindings()
    {
        var (window, _, side) = Layout();
        var ran = 0;
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => ran++), KeyGesture.Parse("Escape")));
        Settle(window);

        Press(window, Key.Escape);

        Assert.That(ran, Is.EqualTo(1));
    }

    [Test]
    public void IsBoundInWindow_TellsAKeySomethingElseWouldRun()
    {
        var (window, field, side) = Layout();
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => { }), KeyGesture.Parse("Escape")));
        ((StackPanel)field.VisualParent).InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => { }),
            KeyGesture.Parse("F6")));
        field.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => { }), KeyGesture.Parse("F7")));
        Settle(window);

        var elsewhere = CommandManager.IsBoundInWindow(field, Args(field, Key.Escape));
        var around = CommandManager.IsBoundInWindow(field, Args(field, Key.F6));
        var own = CommandManager.IsBoundInWindow(field, Args(field, Key.F7));
        var unbound = CommandManager.IsBoundInWindow(field, Args(field, Key.Enter));

        Assert.That((elsewhere, around, own, unbound), Is.EqualTo((true, true, false, false)));
    }

    // A field being typed in keeps the keys that type: a letter, Esc or an AltGr character stays with it, Ctrl with a
    // letter or a function key goes on to the window's bindings.
    [Test]
    public void WhileTyping_OnlyKeysThatDoNotType_ReachTheWindow()
    {
        var field = new TextBox();
        var side = new StackPanel();
        var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { field, side } } };
        var ran = new System.Collections.Generic.List<string>();
        foreach (var gesture in new[] { "W", "Escape", "Ctrl+W", "F5", "Ctrl+Alt+E" })
        {
            side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => ran.Add(gesture)), KeyGesture.Parse(gesture)));
        }

        Settle(window);
        FocusManager.Focus(field);

        Press(field, Key.W);
        Press(field, Key.E, InputModifiers.LeftControl | InputModifiers.LeftAlt);
        Press(field, Key.W, InputModifiers.LeftControl);
        Press(field, Key.F5);
        Press(field, Key.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.EqualTo(new[] { "Ctrl+W", "F5" }));
            Assert.That(FocusManager.Focused, Is.Null, "Esc in the field only leaves it");
        });
    }

    // A field placed inside something with keys of its own - a minus that zooms a map out - keeps the keys that type:
    // the holder's binding does not run on them; Ctrl with a letter and the field's own binding still do.
    [Test]
    public void WhileTyping_TheHolderOfTheField_DoesNotTakeTheKeysThatType()
    {
        var field = new TextBox();
        var holder = new StackPanel { Children = { field } };
        var window = new Window { Width = 400, Height = 300, Content = holder };
        var ran = new System.Collections.Generic.List<string>();
        foreach (var gesture in new[] { "OemMinus", "NumPadAdd", "Ctrl+W" })
        {
            holder.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => ran.Add(gesture)),
                KeyGesture.Parse(gesture)));
        }

        field.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => ran.Add("own Space")), KeyGesture.Parse("Space")));
        Settle(window);
        FocusManager.Focus(field);

        var minus = Press(field, Key.OemMinus);
        Press(field, Key.NumPadAdd);
        Press(field, Key.W, InputModifiers.LeftControl);
        Press(field, Key.Space);

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.EqualTo(new[] { "Ctrl+W", "own Space" }));
            Assert.That(minus.Handled, Is.False, "the minus goes on to be typed");
        });
    }

    // The same through a class's own command gesture, as a map zooms out on a minus; Shift and AltGr type too.
    [Test]
    public void WhileTyping_AHoldersClassGesture_DoesNotTakeTheKeysThatType()
    {
        var field = new TextBox();
        var holder = new ZoomingPanel { Children = { field } };
        var window = new Window { Width = 400, Height = 300, Content = holder };
        Settle(window);
        FocusManager.Focus(field);

        var minus = Press(field, Key.OemMinus);
        Press(field, Key.OemMinus, InputModifiers.LeftShift);
        Press(field, Key.OemMinus, InputModifiers.LeftControl | InputModifiers.RightAlt);

        Assert.Multiple(() =>
        {
            Assert.That(holder.Zoomed, Is.Zero);
            Assert.That(minus.Handled, Is.False);
        });
    }

    // Away from a field that types - focus on a button, a read-only field, the key raised on the holder itself - the
    // holder's gesture runs as before.
    [Test]
    public void AwayFromTyping_TheHoldersGestureRuns()
    {
        var field = new TextBox();
        var readOnly = new TextBox { IsReadOnly = true };
        var button = new Button();
        var holder = new ZoomingPanel { Children = { field, readOnly, button } };
        var window = new Window { Width = 400, Height = 300, Content = holder };
        Settle(window);

        FocusManager.Focus(button);
        Press(button, Key.OemMinus);
        FocusManager.Focus(readOnly);
        Press(readOnly, Key.OemMinus);
        FocusManager.Focus(field);
        Press(holder, Key.OemMinus);

        Assert.That(holder.Zoomed, Is.EqualTo(3));
    }

    private sealed class ZoomingPanel : StackPanel
    {
        private static readonly RoutedCommand ZoomOut = new(nameof(ZoomOut), typeof(ZoomingPanel),
            new KeyGesture(Key.OemMinus, ModifierKeys.None));

        static ZoomingPanel()
        {
            CommandManager.RegisterClassCommandBinding(typeof(ZoomingPanel), new CommandBinding(ZoomOut,
                static (sender, _) => ((ZoomingPanel)sender).Zoomed++, static (_, e) => e.CanExecute = true));
        }

        public int Zoomed { get; private set; }
    }

    // The way up goes first all the way to the window: its cancel button and the keyboard's Tab navigation keep their
    // keys; a binding elsewhere gets only what is left.
    [Test]
    public void TheWindowsCancelButtonAndTabNavigation_GoBeforeABindingElsewhere()
    {
        var field = new Button();
        var cancel = new Button { IsCancel = true };
        var side = new StackPanel { Children = { new Button() } };
        var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { field, cancel, side } } };
        var cancelled = 0;
        cancel.Click += (_, _) => cancelled++;
        var ran = 0;
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => ran++), KeyGesture.Parse("Escape")));
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => ran++), KeyGesture.Parse("Tab")));
        Settle(window);
        FocusManager.Focus(field);

        Press(field, Key.Escape);
        Press(field, Key.Tab);

        Assert.That((cancelled, ran), Is.EqualTo((1, 0)));
    }

    [Test]
    public void AnEscapeNoBindingTakes_StillClearsTheFocus()
    {
        var (window, field, side) = Layout();
        var can = false;
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => { }, () => can), KeyGesture.Parse("Escape")));
        Settle(window);
        FocusManager.Focus(field);

        Press(field, Key.Escape);

        Assert.That(FocusManager.Focused, Is.Null);
    }

    private sealed class Commands
    {
        public int Cancelled { get; private set; }

        public AdamantiumCommand Cancel => new(() => Cancelled++);
    }

    // An overlay window answers Enter with its default button, as a window does - its card is a tree of its own, which
    // the window's handling of the key never sees. The content behind it hears nothing of the key.
    [Test]
    public void EnterInAModalOverlayWindow_PressesItsDefaultButton()
    {
        var (window, _, side) = Layout();
        var behind = 0;
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => behind++), KeyGesture.Parse("Enter")));
        var (dialog, field) = Dialog(window, out var made, out _);

        FromTheKeyboard(field, Key.Enter);

        Assert.That((made(), behind, dialog.IsOpen), Is.EqualTo((1, 0, true)));
    }

    // The keyboard left in the content under a modal window - nothing in the card took it, or code put it there - is
    // still shut out of that content, as the scrim shuts the pointer out: the key is the modal's, and the keyboard goes
    // back into it.
    [Test]
    public void AKeyPressedWithTheFocusBehindAModalWindow_IsTheModals()
    {
        var (window, behindField, side) = Layout();
        var behind = 0;
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => behind++), KeyGesture.Parse("Enter")));
        side.InputBindings.Add(new KeyBinding(new AdamantiumCommand(() => behind++), KeyGesture.Parse("F6")));
        var (_, field) = Dialog(window, out var made, out _);
        FocusManager.Focus(behindField);

        FromTheKeyboard(behindField, Key.F6);
        var focus = FocusManager.Focused;
        FromTheKeyboard(behindField, Key.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(behind, Is.Zero, "the content under the modal runs none of its bindings");
            Assert.That(focus, Is.SameAs(field), "the keyboard is back in the modal");
            Assert.That(made(), Is.EqualTo(1), "Enter is the modal's default button's");
        });
    }

    // The focus put in the content under a modal window - a menu closing after the window opened hands it back to where
    // the menu was opened from - goes back into the modal once that change is done.
    [Test]
    public void TheFocusPutUnderAModalWindow_GoesBackIntoIt()
    {
        var (window, behindField, _) = Layout();
        var (_, field) = Dialog(window, out _, out _);

        FocusManager.Focus(behindField);
        LoopSignal.Drain();

        Assert.That(FocusManager.Focused, Is.SameAs(field));
    }

    // A window that may not close by Escape does not close by its Cancel button pressed by Escape either.
    [Test]
    public void EscapeInAnOverlayWindowThatKeepsOpenOnIt_PressesNoCancelButton()
    {
        var (window, _, _) = Layout();
        var (dialog, field) = Dialog(window, out _, out var cancelled);
        dialog.CloseByEscape = false;

        FromTheKeyboard(field, Key.Escape);

        Assert.That((cancelled(), dialog.IsOpen), Is.EqualTo((0, true)));
    }

    private static (OverlayWindow Dialog, TextBox Field) Dialog(Window window, out Func<int> made, out Func<int> cancelled)
    {
        var field = new TextBox();
        var makes = 0;
        var cancels = 0;
        var dialog = new OverlayWindow
        {
            IsModal = true,
            Content = new StackPanel
            {
                Children =
                {
                    field,
                    new Button { IsDefault = true, Command = new AdamantiumCommand(() => makes++) },
                    new Button { IsCancel = true, Command = new AdamantiumCommand(() => cancels++) }
                }
            }
        };
        window.ShowOverlayWindow(dialog);
        Settle(window);
        window.PopupLayer.UpdateLayout(new Size<double>(400, 300));
        Settle(window);
        made = () => makes;
        cancelled = () => cancels;
        return (dialog, field);
    }

    // As the keyboard raises a key: the preview down from the window, then the key up from where it was pressed.
    private static void FromTheKeyboard(IInputComponent at, Key key)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, InputModifiers.None, 0) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        at.RaiseEvent(args);
        args.RoutedEvent = Keyboard.KeyDownEvent;
        at.RaiseEvent(args);
    }

    private static (Window Window, Button Field, StackPanel Side) Layout()
    {
        var field = new Button();
        var side = new StackPanel { Children = { new Button() } };
        var window = new Window
        {
            Width = 400, Height = 300,
            Content = new StackPanel { Children = { new StackPanel { Children = { field } }, side } }
        };
        Settle(window);
        return (window, field, side);
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static KeyEventArgs Args(IInputComponent at, Key key, InputModifiers modifiers = InputModifiers.None) =>
        new(KeyboardDevice.CurrentDevice, key, modifiers, 0) { RoutedEvent = Keyboard.KeyDownEvent, Source = at };

    private static KeyEventArgs Press(IInputComponent at, Key key, InputModifiers modifiers = InputModifiers.None)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0) { RoutedEvent = Keyboard.KeyDownEvent };
        at.RaiseEvent(args);
        return args;
    }
}
