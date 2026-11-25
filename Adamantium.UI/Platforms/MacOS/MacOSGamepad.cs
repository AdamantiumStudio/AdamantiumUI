using System;
using System.Threading.Tasks;
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

    private const int LowMotor = 0;
    private const int HighMotor = 1;
    private const int LeftTriggerMotor = 2;
    private const int RightTriggerMotor = 3;
    private const float Deep = 0;
    private const float Fine = 1;

    private readonly MacOSGamepadMotors _motors;
    private readonly bool _hasMotorsApart;
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

        // One with no motors told by their place runs them together.
        var low = MotorAt(GCController.LeftHandle, Deep);
        var high = MotorAt(GCController.RightHandle, Fine);
        _hasMotorsApart = low != null || high != null;
        _motors = new MacOSGamepadMotors(_hasMotorsApart ? low : MotorAt(GCController.DefaultLocality, Deep), high,
            MotorAt(GCController.LeftTrigger, Fine), MotorAt(GCController.RightTrigger, Fine));
    }

    private MacOSHapticMotor MotorAt(IntPtr locality, float sharpness) => GCController.HasMotorAt(_controller, locality)
        ? new MacOSHapticMotor(_controller, locality, sharpness)
        : null;

    public override void SetVibration(float lowFrequency, float highFrequency)
    {
        _motors.Run(LowMotor, _hasMotorsApart ? lowFrequency : Math.Max(lowFrequency, highFrequency));
        _motors.Run(HighMotor, highFrequency);
    }

    public override void SetTriggerVibration(float left, float right)
    {
        _motors.Run(LeftTriggerMotor, left);
        _motors.Run(RightTriggerMotor, right);
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
        if (_controller == IntPtr.Zero)
            return;

        // A motor may still be starting on it: the controller goes when the last is let go of.
        _motors.Close();
        var controller = _controller;
        _controller = IntPtr.Zero;
        _motors.Applying.ContinueWith(_ => NSObject.Release(controller), TaskScheduler.Default);
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
