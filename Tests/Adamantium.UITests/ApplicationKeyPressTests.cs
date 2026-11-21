using System.Collections.Generic;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A key the application presses, as an on-screen keyboard does, is down until the application releases it,
/// though the system's keyboard has every key up.</summary>
[TestFixture]
[NonParallelizable]
public class ApplicationKeyPressTests
{
    private INativeKeyboard _platformBefore;
    private IInputComponent _focusBefore;

    [SetUp]
    public void SetUp()
    {
        _platformBefore = Keyboard.Platform;
        _focusBefore = KeyboardDevice.CurrentDevice.FocusedComponent;
        Keyboard.Platform = new AllKeysUpKeyboard();
    }

    [TearDown]
    public void TearDown()
    {
        Keyboard.Platform = _platformBefore;
        KeyboardDevice.CurrentDevice.SetFocusedElement(_focusBefore);
    }

    [Test]
    public void ACtrlTheApplicationHolds_IsInTheModifiers_UntilItIsReleased()
    {
        var device = KeyboardDevice.CurrentDevice;
        var target = new Border();

        Press(device, target, Key.LeftCtrl, RawKeyboardEventType.KeyDown, KeyPressOrigin.Application);
        var whileHeld = device.Modifiers;
        var staticWhileHeld = Keyboard.Modifiers;
        Press(device, target, Key.LeftCtrl, RawKeyboardEventType.KeyUp, KeyPressOrigin.Application);

        Assert.Multiple(() =>
        {
            Assert.That(whileHeld, Is.EqualTo(InputModifiers.LeftControl));
            Assert.That(staticWhileHeld, Is.EqualTo(InputModifiers.LeftControl));
            Assert.That(device.Modifiers, Is.EqualTo(InputModifiers.None), "released, it is up again");
        });
    }

    [Test]
    public void AKeyTheApplicationPresses_ArrivesDown_AndComesUpUp()
    {
        var device = KeyboardDevice.CurrentDevice;
        var target = new Border();
        var downs = new List<bool>();
        var ups = new List<bool>();
        target.KeyDown += (_, e) => downs.Add(e.IsDown);
        target.KeyUp += (_, e) => ups.Add(e.IsDown);

        Press(device, target, Key.A, RawKeyboardEventType.KeyDown, KeyPressOrigin.Application);
        Press(device, target, Key.A, RawKeyboardEventType.KeyUp, KeyPressOrigin.Application);

        Assert.Multiple(() =>
        {
            Assert.That(downs, Has.Count.EqualTo(1).And.All.True);
            Assert.That(ups, Has.Count.EqualTo(1).And.All.False);
        });
    }

    [Test]
    public void AKeyTheApplicationReleases_WithNothingFocused_IsUp()
    {
        var device = KeyboardDevice.CurrentDevice;

        Press(device, new Border(), Key.LeftCtrl, RawKeyboardEventType.KeyDown, KeyPressOrigin.Application);
        device.SetFocusedElement(null);
        device.ProcessEvent(new RawKeyboardEventArgs(Key.LeftCtrl, RawKeyboardEventType.KeyUp,
            new KeyPressInfo { CurrentState = KeyState.Up }, InputModifiers.LeftControl, 0, KeyPressOrigin.Application));

        Assert.That(device.IsKeyDown(Key.LeftCtrl), Is.False, "no element took the release, the key is still up");
    }

    [Test]
    public void AKeyFromTheSystem_LeavesItsStateToThePlatform()
    {
        var device = KeyboardDevice.CurrentDevice;
        var target = new Border();

        Press(device, target, Key.LeftShift, RawKeyboardEventType.KeyDown, KeyPressOrigin.System);
        var whileDown = device.IsKeyDown(Key.LeftShift);
        Press(device, target, Key.LeftShift, RawKeyboardEventType.KeyUp, KeyPressOrigin.System);

        Assert.That(whileDown, Is.False, "the platform says every key is up");
    }

    private static void Press(KeyboardDevice device, IInputComponent target, Key key, RawKeyboardEventType type,
        KeyPressOrigin origin)
    {
        var state = type == RawKeyboardEventType.KeyDown ? KeyState.Down : KeyState.Up;
        device.SetFocusedElement(target);
        device.ProcessEvent(new RawKeyboardEventArgs(key, type, new KeyPressInfo { CurrentState = state },
            device.Modifiers, 0, origin));
    }
}
