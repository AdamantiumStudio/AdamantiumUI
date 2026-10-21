using System;
using System.Collections.Generic;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Animation;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A routed command goes from its target - the focus outside every focus scope, or one named - up the tree to
/// the first element that handles it; keys ask for it; sources are told to ask again at most once a frame.</summary>
[TestFixture]
public class RoutedCommandTests
{
    private static readonly RoutedCommand Shout = new("Shout", typeof(RoutedCommandTests),
        new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift));

    private static readonly RoutedCommand Whisper = new("Whisper", typeof(RoutedCommandTests));

    static RoutedCommandTests()
    {
        CommandManager.RegisterClassCommandBinding(typeof(ClassHandled),
            new CommandBinding(Whisper, (s, _) => ((ClassHandled)s).Runs++, (_, e) => e.CanExecute = true));
    }

    [TearDown]
    public void LetGoOfTheFocus() => FocusManager.ResetFocus();

    [Test]
    public void ACommand_RunsOnTheFirstElementUpFromTheFocusThatHandlesIt()
    {
        var (outer, inner, field) = Tree();
        var ran = new List<object>();
        CommandManager.GetCommandBindings(inner).Add(new CommandBinding(Shout, (s, _) => ran.Add(s)));
        CommandManager.GetCommandBindings(outer).Add(new CommandBinding(Shout, (s, _) => ran.Add(s)));
        FocusManager.Focus(field);

        Shout.Execute(null);

        Assert.That(ran, Is.EqualTo(new object[] { inner }));
    }

    [Test]
    public void AnElementsOwnBinding_GoesBeforeItsClassBinding_AndAClassBindingServesDerivedTypes()
    {
        var derived = new DerivedHandled();
        var own = 0;
        FocusManager.Focus(derived);

        Whisper.Execute(null);
        CommandManager.GetCommandBindings(derived).Add(new CommandBinding(Whisper, (_, _) => own++));
        Whisper.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(derived.Runs, Is.EqualTo(1));
            Assert.That(own, Is.EqualTo(1));
        });
    }

    [Test]
    public void ACommandThatCannotRun_IsNotRun_AndItsStateIsWhatTheHandlerSays()
    {
        var (_, inner, field) = Tree();
        var ran = 0;
        CommandManager.GetCommandBindings(inner).Add(new CommandBinding(Shout, (_, _) => ran++, (_, e) =>
        {
            e.CanExecute = false;
            e.IsChecked = true;
            e.Value = "bold";
        }));
        FocusManager.Focus(field);

        var state = Shout.Query(null, field);
        var executed = Shout.Execute(null, field);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(new CommandState(false, true, "bold")));
            Assert.That(executed, Is.False);
            Assert.That(ran, Is.Zero);
        });
    }

    [Test]
    public void ANamedTarget_GoesBeforeTheFocus()
    {
        var (_, inner, field) = Tree();
        var (_, otherInner, _) = Tree();
        var ran = new List<object>();
        CommandManager.GetCommandBindings(inner).Add(new CommandBinding(Shout, (s, _) => ran.Add(s)));
        CommandManager.GetCommandBindings(otherInner).Add(new CommandBinding(Shout, (s, _) => ran.Add(s)));
        FocusManager.Focus(field);

        CommandManager.ExecuteFrom(Shout, null, field, otherInner);

        Assert.That(ran, Is.EqualTo(new object[] { otherInner }));
    }

    // A tool window over the window - an overlay card - is its host's, as a menu is its owner's: what is run in it goes on
    // to the field the host's focus is in.
    [Test]
    public void ACommandRunInAnOverlayWindow_GoesToTheFieldWithTheHostsFocus()
    {
        var (outer, inner, field) = Tree();
        var host = new Window { Width = 400, Height = 300, Content = outer };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(host);
        var ran = new List<object>();
        CommandManager.GetCommandBindings(inner).Add(new CommandBinding(Shout, (s, _) => ran.Add(s)));
        FocusManager.Focus(field);
        var toolButton = new Button();
        var tool = new StackPanel { Children = { toolButton } };
        FocusManager.SetIsFocusScope(tool, true);
        var overlay = new OverlayWindow
        {
            Template = new Adamantium.UI.Core.Templates.ControlTemplate(() =>
                new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = tool }),
        };

        host.ShowOverlayWindow(overlay);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(host);
        FocusManager.Focus(toolButton);
        CommandManager.ExecuteFrom(Shout, null, toolButton, null);
        overlay.Close();

        Assert.That(ran, Is.EqualTo(new object[] { inner }));
    }

    // Opened from a place whose commands go to a target of their own - a ribbon's to its document - it keeps that target:
    // with no field focused, what is run in it goes there.
    [Test]
    public void AnOverlayWindowOpenedFromAPlaceWithACommandTarget_SendsItsCommandsThere()
    {
        var (outer, inner, _) = Tree();
        var opener = new Button();
        var ribbon = new StackPanel { Children = { opener } };
        CommandManager.SetCommandTarget(ribbon, inner);
        outer.Children.Add(ribbon);
        var host = new Window { Width = 400, Height = 300, Content = outer };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(host);
        var ran = new List<object>();
        CommandManager.GetCommandBindings(inner).Add(new CommandBinding(Shout, (s, _) => ran.Add(s)));
        FocusManager.Focus(opener);
        var toolButton = new Button();
        var tool = new StackPanel { Children = { toolButton } };
        FocusManager.SetIsFocusScope(tool, true);
        var overlay = new OverlayWindow
        {
            Template = new Adamantium.UI.Core.Templates.ControlTemplate(() =>
                new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = tool }),
        };

        host.ShowOverlayWindow(overlay);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(host);
        FocusManager.Focus(toolButton);
        CommandManager.ExecuteFrom(Shout, null, toolButton, null);
        overlay.Close();

        Assert.That(ran, Is.EqualTo(new object[] { inner }));
    }

    [Test]
    public void TheFocusInsideAFocusScope_LeavesCommandsGoingToTheFieldOutside()
    {
        var (outer, inner, field) = Tree();
        var menuButton = new Button();
        var menu = new StackPanel { Children = { menuButton } };
        FocusManager.SetIsFocusScope(menu, true);
        outer.Children.Add(menu);
        var ran = new List<object>();
        CommandManager.GetCommandBindings(inner).Add(new CommandBinding(Shout, (s, _) => ran.Add(s)));
        FocusManager.Focus(field);

        FocusManager.Focus(menuButton);
        CommandManager.ExecuteFrom(Shout, null, menuButton, null);

        Assert.Multiple(() =>
        {
            Assert.That(FocusManager.GetFocusedElement(outer), Is.SameAs(field));
            Assert.That(ran, Is.EqualTo(new object[] { inner }));
        });
    }

    [Test]
    public void APreviewHandlerThatHandlesIt_StopsTheCommand()
    {
        var (outer, inner, field) = Tree();
        var ran = 0;
        CommandManager.GetCommandBindings(inner).Add(new CommandBinding(Shout, (_, _) => ran++));
        outer.AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler((_, e) => e.Handled = true));

        Shout.Execute(null, field);

        Assert.That(ran, Is.Zero);
    }

    [Test]
    public void TheCommandsOwnKeys_RunItWhereABindingOnTheWayHandlesIt()
    {
        var (_, inner, field) = Tree();
        var ran = 0;
        CommandManager.GetCommandBindings(inner).Add(new CommandBinding(Shout, (_, _) => ran++));
        FocusManager.Focus(field);

        var plain = Press(field, Key.S, InputModifiers.LeftControl);
        var gesture = Press(field, Key.S, InputModifiers.RightControl | InputModifiers.LeftShift);

        Assert.Multiple(() =>
        {
            Assert.That(plain.Handled, Is.False, "Ctrl+S is not the command's key");
            Assert.That(gesture.Handled, Is.True);
            Assert.That(ran, Is.EqualTo(1));
        });
    }

    [Test]
    public void AKeyBinding_RunsAPlainCommand()
    {
        var (outer, _, field) = Tree();
        var ran = 0;
        CommandManager.GetInputBindings(outer).Add(new KeyBinding(new AdamantiumCommand(() => ran++),
            KeyGesture.Parse("Ctrl+F5")));
        FocusManager.Focus(field);

        Press(field, Key.F5, InputModifiers.LeftControl);

        Assert.That(ran, Is.EqualTo(1));
    }

    [Test]
    public void ABindingAmongTheBehaviors_RunsItsHandler_WhenTheHandlerCan()
    {
        var (_, inner, field) = Tree();
        var can = false;
        var ran = 0;
        var handler = new AdamantiumCommand(() => ran++, () => can);
        var binding = new CommandBinding { Command = Shout, Handler = handler };
        inner.Behaviors.Add(binding);

        var refused = Shout.Execute(null, field);
        can = true;
        var run = Shout.Execute(null, field);
        inner.Behaviors.Remove(binding);
        var gone = Shout.Query(null, field);

        Assert.Multiple(() =>
        {
            Assert.That((refused, run, ran), Is.EqualTo((false, true, 1)));
            Assert.That(gone, Is.EqualTo(CommandState.Unhandled));
        });
    }

    [Test]
    public void AHandlerThatChangesItsMind_HasSourcesAskAgain()
    {
        var heard = 0;
        EventHandler listener = (_, _) => heard++;
        var handler = new AdamantiumCommand(() => { });
        var binding = new CommandBinding { Command = Shout, Handler = handler };
        Shout.CanExecuteChanged += listener;
        try
        {
            AnimationManager.Tick(0.016);
            var before = heard;
            handler.RaiseCanExecuteChanged();
            AnimationManager.Tick(0.016);

            Assert.That(heard - before, Is.EqualTo(1));
            GC.KeepAlive(binding);
        }
        finally
        {
            Shout.CanExecuteChanged -= listener;
        }
    }

    [Test]
    public void ManyAsksToRequery_MakeOneOnTheNextFrame()
    {
        var heard = 0;
        EventHandler listener = (_, _) => heard++;
        Shout.CanExecuteChanged += listener;
        try
        {
            CommandManager.InvalidateRequerySuggested();
            CommandManager.InvalidateRequerySuggested();
            var before = heard;
            AnimationManager.Tick(0.016);
            AnimationManager.Tick(0.016);

            Assert.That((before, heard), Is.EqualTo((0, 1)));
        }
        finally
        {
            Shout.CanExecuteChanged -= listener;
        }
    }

    [Test]
    public void AnAskDroppedWithTheAnimations_IsMadeAgain()
    {
        var heard = 0;
        EventHandler listener = (_, _) => heard++;
        Shout.CanExecuteChanged += listener;
        try
        {
            CommandManager.InvalidateRequerySuggested();
            AnimationManager.Reset();
            AnimationManager.Tick(0.016);
            CommandManager.InvalidateRequerySuggested();
            AnimationManager.Tick(0.016);

            Assert.That(heard, Is.EqualTo(1));
        }
        finally
        {
            Shout.CanExecuteChanged -= listener;
        }
    }

    [TestCase("Ctrl+Shift+K", Key.K, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+K")]
    [TestCase("control+1", Key.D1, ModifierKeys.Control, "Ctrl+1")]
    [TestCase("Cmd+Delete", Key.Delete, ModifierKeys.Meta, "Meta+Delete")]
    public void AGesture_ReadsAndWritesAsText(string text, Key key, ModifierKeys modifiers, string written)
    {
        var gesture = KeyGesture.Parse(text);

        Assert.That((gesture, gesture.ToString()), Is.EqualTo((new KeyGesture(key, modifiers), written)));
    }

    [TestCase("")]
    [TestCase("Ctrl+")]
    [TestCase("Hyper+K")]
    [TestCase("Ctrl+65")]
    public void NotAGesture_IsRefused(string text)
    {
        Assert.Throws<FormatException>(() => KeyGesture.Parse(text));
    }

    private static (StackPanel Outer, StackPanel Inner, Button Field) Tree()
    {
        var field = new Button();
        var inner = new StackPanel { Children = { field } };
        var outer = new StackPanel { Children = { inner } };
        return (outer, inner, field);
    }

    private static KeyEventArgs Press(IInputComponent at, Key key, InputModifiers modifiers)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0) { RoutedEvent = Keyboard.KeyDownEvent };
        at.RaiseEvent(args);
        return args;
    }

    private class ClassHandled : Button
    {
        public int Runs { get; set; }
    }

    private sealed class DerivedHandled : ClassHandled
    {
    }
}
