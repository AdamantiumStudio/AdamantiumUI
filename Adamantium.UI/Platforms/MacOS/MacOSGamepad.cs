using System;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.GameController;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.Multiverse.Input;

namespace Adamantium.UI.Platforms.MacOS;

internal sealed class MacOSGamepad : Gamepad
{
    private static readonly (GamepadButton Button, IntPtr Selector)[] Buttons =
    [
        (GamepadButton.A, Property("buttonA")), (GamepadButton.B, Property("buttonB")),
        (GamepadButton.X, Property("buttonX")), (GamepadButton.Y, Property("buttonY")),
        (GamepadButton.LeftShoulder, Property("leftShoulder")), (GamepadButton.RightShoulder, Property("rightShoulder")),
        (GamepadButton.Back, Property("buttonOptions")), (GamepadButton.Start, Property("buttonMenu")),
        (GamepadButton.LeftThumb, Property("leftThumbstickButton")),
        (GamepadButton.RightThumb, Property("rightThumbstickButton")), (GamepadButton.Guide, Property("buttonHome"))
    ];

    private static readonly IntPtr DpadProperty = Property("dpad");

    private static readonly (GamepadButton Button, IntPtr Selector)[] Directions =
    [
        (GamepadButton.DpadUp, Property("up")), (GamepadButton.DpadDown, Property("down")),
        (GamepadButton.DpadLeft, Property("left")), (GamepadButton.DpadRight, Property("right"))
    ];

    private static readonly IntPtr LeftStickProperty = Property("leftThumbstick");
    private static readonly IntPtr RightStickProperty = Property("rightThumbstick");
    private static readonly IntPtr LeftTriggerProperty = Property("leftTrigger");
    private static readonly IntPtr RightTriggerProperty = Property("rightTrigger");

    private IntPtr _controller;

    internal MacOSGamepad(IntPtr controller)
    {
        _controller = NSObject.Retain(controller);
        Name = GCController.NameOf(controller) ?? string.Empty;
        Face = FaceOf(GCController.CategoryOf(controller));
        var gamepad = GCController.GamepadOf(controller);
        var supported = GamepadButton.DpadUp | GamepadButton.DpadDown | GamepadButton.DpadLeft | GamepadButton.DpadRight;
        foreach (var (button, selector) in Buttons)
        {
            if (GCController.PartOf(gamepad, selector) != IntPtr.Zero)
                supported |= button;
        }

        SupportedButtons = supported;
    }

    internal IntPtr Controller => _controller;

    public override string Name { get; }

    public override GamepadFace Face { get; }

    public override GamepadButton SupportedButtons { get; }

    public override GamepadState GetState()
    {
        if (_controller == IntPtr.Zero)
            return default;

        var pool = ObjCRuntime.PushAutoreleasePool();
        try
        {
            return Read(GCController.GamepadOf(_controller));
        }
        finally
        {
            ObjCRuntime.PopAutoreleasePool(pool);
        }
    }

    internal void Disconnect()
    {
        NSObject.Release(_controller);
        _controller = IntPtr.Zero;
    }

    private static GamepadState Read(IntPtr gamepad)
    {
        if (gamepad == IntPtr.Zero)
            return default;

        var buttons = GamepadButton.None;
        foreach (var (button, selector) in Buttons)
        {
            if (GCController.IsPressed(GCController.PartOf(gamepad, selector)))
                buttons |= button;
        }

        var dpad = GCController.PartOf(gamepad, DpadProperty);
        foreach (var (button, selector) in Directions)
        {
            if (GCController.IsPressed(GCController.PartOf(dpad, selector)))
                buttons |= button;
        }

        return new GamepadState
        {
            IsConnected = true,
            Buttons = buttons,
            LeftThumb = StickOf(gamepad, LeftStickProperty),
            RightThumb = StickOf(gamepad, RightStickProperty),
            LeftTrigger = GCController.ValueOf(GCController.PartOf(gamepad, LeftTriggerProperty)),
            RightTrigger = GCController.ValueOf(GCController.PartOf(gamepad, RightTriggerProperty))
        };
    }

    private static Vector2F StickOf(IntPtr gamepad, IntPtr selector)
    {
        var stick = GCController.PartOf(gamepad, selector);
        return new Vector2F(GCController.ValueOf(GCController.XAxisOf(stick)), GCController.ValueOf(GCController.YAxisOf(stick)));
    }

    internal static GamepadFace FaceOf(string category) => category switch
    {
        null => GamepadFace.Unknown,
        _ when category.Contains("Dual", StringComparison.OrdinalIgnoreCase) => GamepadFace.PlayStation,
        _ when category.Contains("Xbox", StringComparison.OrdinalIgnoreCase) => GamepadFace.Xbox,
        _ when category.Contains("Switch", StringComparison.OrdinalIgnoreCase) ||
               category.Contains("Joy-Con", StringComparison.OrdinalIgnoreCase) => GamepadFace.Nintendo,
        _ => GamepadFace.Unknown
    };

    private static IntPtr Property(string name) => ObjCRuntime.RegisterSelector(name);
}
