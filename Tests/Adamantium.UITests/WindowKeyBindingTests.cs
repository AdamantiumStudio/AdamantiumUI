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
