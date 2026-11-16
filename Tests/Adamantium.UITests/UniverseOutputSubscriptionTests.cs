using System.Linq;
using Adamantium.Core.Events;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Universes;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class UniverseOutputSubscriptionTests
{
    [Test]
    public void AfterSwitchingPanels_TheOldPanelsKeys_NoLongerReachTheOutput()
    {
        var first = new RenderTargetPanel();
        var second = new RenderTargetPanel();
        var output = Output(first);

        ((UniverseOutput)output).SwitchContext(new OutputContext(second));
        Press(first);

        Assert.That(output.Input.KeyboardInputs, Is.Empty);

        Press(second);

        Assert.That(output.Input.KeyboardInputs, Has.Count.EqualTo(1));
    }

    [Test]
    public void ADisposedOutput_NoLongerListensToItsPanel()
    {
        var panel = new RenderTargetPanel();
        var output = Output(panel);

        output.Dispose();
        Press(panel);

        Assert.That(output.Input.KeyboardInputs, Is.Empty);
    }

    [Test]
    public void AKeyTypedIntoThePanel_IsSpentOnTheUniverse()
    {
        var panel = new RenderTargetPanel();
        var output = Output(panel);

        var arrow = Press(panel, Key.UpArrow);

        Assert.Multiple(() =>
        {
            Assert.That(output.Input.KeyboardInputs, Has.Count.EqualTo(1));
            Assert.That(arrow.Handled, Is.True, "focus navigation must not take the camera's arrow off the panel");
        });
    }

    [Test]
    public void WhileTheUniverseTakesNoKeyboard_TheKeyIsNotSpent()
    {
        var panel = new RenderTargetPanel();
        var output = Output(panel);
        output.Input.IsKeyboardEnabled = false;

        Assert.That(Press(panel, Key.UpArrow).Handled, Is.False);
    }

    [TestCase(true, ExpectedResult = true)]
    [TestCase(false, ExpectedResult = false)]
    public bool Tab_IsTheUniversesWhileThePanelAcceptsIt(bool acceptsTab)
    {
        // Read below the root, where focus navigation answers it.
        var panel = new RenderTargetPanel { AcceptsTab = acceptsTab };
        var host = new Border { Child = panel };
        _ = new Border { Child = host };
        Output(panel);
        bool? handledOnArrival = null;
        host.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler((_, e) => handledOnArrival = e.Handled), handledEventsToo: true);

        Press(panel, Key.Tab);

        return handledOnArrival == true;
    }

    [Test]
    public void CtrlTab_LeavesAPanelThatAcceptsTab()
    {
        var panel = new RenderTargetPanel { Height = 50 };
        var next = new Button();
        var stack = new StackPanel();
        stack.Children.Add(panel);
        stack.Children.Add(next);
        var window = new Window { Width = 200, Height = 100, Content = stack };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        var output = Output(panel);
        FocusManager.Focus(panel);

        try
        {
            var key = Press(panel, Key.Tab, InputModifiers.LeftControl);

            Assert.Multiple(() =>
            {
                Assert.That(FocusManager.Focused, Is.SameAs(next));
                Assert.That(key.Handled, Is.True);
                Assert.That(output.Input.KeyboardInputs, Is.Empty, "the key that leaves is not the universe's");
            });
        }
        finally
        {
            FocusManager.ResetFocus();
        }
    }

    [Test]
    public void AKeyTypedIntoAControlOverThePicture_IsNotTheUniverses()
    {
        var panel = new RenderTargetPanel();
        var button = new Button();
        panel.Children.Add(button);
        var output = Output(panel);

        var key = Press(button, Key.W);

        Assert.Multiple(() =>
        {
            Assert.That(output.Input.KeyboardInputs, Is.Empty);
            Assert.That(key.Handled, Is.False);
        });
    }

    // The ribbon takes Alt for its key tips; the universe hears it all the same - Alt with a drag orbits its camera - but
    // not a key someone else took that is no modifier.
    [Test]
    public void AModifierHandledOnTheWay_StillReachesTheUniverse_AnotherKeyDoesNot()
    {
        var panel = new RenderTargetPanel();
        var output = Output(panel);

        Press(panel, Key.LeftAlt, handled: true);
        Press(panel, Key.W, handled: true);

        Assert.That(output.Input.KeyboardInputs.Select(input => input.Key), Is.EqualTo(new[] { Keys.LeftAlt }));
    }

    // A key let go reaches the universe whoever handled it: one it saw go down would otherwise stay down there.
    [Test]
    public void AKeyLetGo_ReachesTheUniverse_ThoughHandled()
    {
        var panel = new RenderTargetPanel();
        var output = Output(panel);

        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.LeftAlt, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.KeyUpEvent, Handled = true
        };
        panel.RaiseEvent(args);

        Assert.That(output.Input.KeyboardInputs.Select(input => (input.Key, input.InputType)),
            Is.EqualTo(new[] { (Keys.LeftAlt, InputType.Up) }));
    }

    // Alt went down over another control; the right button goes down over the view: Alt is held there as the press
    // comes, so the drag orbits.
    [Test]
    public void APressWithAltHeld_HoldsAltInTheUniverse_WhoeverHadTheKey()
    {
        var panel = new RenderTargetPanel();
        var output = Output(panel);

        panel.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Right,
            InputModifiers.LeftAlt | InputModifiers.RightMouseButton, default)
        {
            RoutedEvent = PointerEvents.PointerPressedEvent
        });

        Assert.That(output.Input.KeyboardInputs.Select(input => (input.Key, input.InputType)),
            Is.EqualTo(new[] { (Keys.LeftAlt, InputType.Down) }));
    }

    private static RenderTargetUniverseOutput Output(RenderTargetPanel panel)
    {
        var output = new RenderTargetUniverseOutput(new EventAggregator(), new OutputContext(panel));
        output.Input = new InputWormhole(output, new GamepadHub(new NoGamepadBackend(), null));
        return output;
    }

    private static KeyEventArgs Press(IObservableComponent target, Key key = Key.W, InputModifiers modifiers = InputModifiers.None,
        bool handled = false)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0)
        {
            RoutedEvent = Keyboard.KeyDownEvent, Handled = handled
        };
        target.RaiseEvent(args);
        return args;
    }
}
