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

    private static RenderTargetUniverseOutput Output(RenderTargetPanel panel)
    {
        var output = new RenderTargetUniverseOutput(new EventAggregator(), new OutputContext(panel));
        output.Input = new InputWormhole(output, new GamepadHub(new NoGamepadBackend(), null));
        return output;
    }

    private static KeyEventArgs Press(IObservableComponent target, Key key = Key.W, InputModifiers modifiers = InputModifiers.None)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, key, modifiers, 0) { RoutedEvent = Keyboard.KeyDownEvent };
        target.RaiseEvent(args);
        return args;
    }
}
