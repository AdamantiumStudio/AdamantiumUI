using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.GameController;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.Multiverse.Input;

namespace Adamantium.UI.Platforms.MacOS;

internal sealed class MacOSGamepad : Gamepad
{
    private static readonly IntPtr GuideProperty = Property("buttonHome");
    private static readonly IntPtr ShareProperty = Property("buttonShare");

    private static readonly (GamepadButton Button, IntPtr Selector)[] Buttons =
    [
        (GamepadButton.A, Property("buttonA")), (GamepadButton.B, Property("buttonB")),
        (GamepadButton.X, Property("buttonX")), (GamepadButton.Y, Property("buttonY")),
        (GamepadButton.LeftShoulder, Property("leftShoulder")), (GamepadButton.RightShoulder, Property("rightShoulder")),
        (GamepadButton.Back, Property("buttonOptions")), (GamepadButton.Start, Property("buttonMenu")),
        (GamepadButton.LeftThumb, Property("leftThumbstickButton")),
        (GamepadButton.RightThumb, Property("rightThumbstickButton")), (GamepadButton.Guide, GuideProperty),
        // An Xbox gamepad's. The Elite's paddles are buttons of their own on its default profile only: on another they
        // press what it maps them to.
        (GamepadButton.Share, ShareProperty),
        (GamepadButton.Paddle1, Property("paddleButton1")), (GamepadButton.Paddle2, Property("paddleButton2")),
        (GamepadButton.Paddle3, Property("paddleButton3")), (GamepadButton.Paddle4, Property("paddleButton4"))
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

    private const float Deep = 0;
    private const float Fine = 1;

    private readonly Lock _lock = new();
    private readonly (GamepadButton Button, IntPtr Selector)[] _buttons;
    private readonly MacOSGamepadMotor _low;
    private readonly MacOSGamepadMotor _high;
    private readonly MacOSGamepadMotor _leftTrigger;
    private readonly MacOSGamepadMotor _rightTrigger;
    private readonly bool _hasMotorsApart;
    private IntPtr _controller;

    internal MacOSGamepad(IntPtr controller)
    {
        _controller = NSObject.Retain(controller);
        Name = GCController.NameOf(controller) ?? string.Empty;
        Face = FaceOf(GCController.CategoryOf(controller));
        var gamepad = GCController.GamepadOf(controller);
        // A property of its kind that is empty now - the Elite's paddles before it is on its default profile - is read all
        // the same.
        _buttons = Array.FindAll(Buttons, entry => GCController.HasProperty(gamepad, entry.Selector));
        foreach (var (_, selector) in _buttons)
        {
            if (selector == GuideProperty || selector == ShareProperty)
                GCController.AlwaysReceive(GCController.PartOf(gamepad, selector));
        }

        // One with no motors told by their place runs them together.
        _hasMotorsApart = GCController.HasMotorAt(controller, GCController.LeftHandle) &&
            GCController.HasMotorAt(controller, GCController.RightHandle);
        _low = MotorAt(_hasMotorsApart ? GCController.LeftHandle : GCController.DefaultLocality, Deep);
        _high = _hasMotorsApart ? MotorAt(GCController.RightHandle, Fine) : new MacOSGamepadMotor(null);
        _leftTrigger = MotorAt(GCController.LeftTrigger, Fine);
        _rightTrigger = MotorAt(GCController.RightTrigger, Fine);
    }

    private MacOSGamepadMotor MotorAt(IntPtr locality, float sharpness) =>
        new(GCController.HasMotorAt(_controller, locality) ? new MacOSHapticMotor(_controller, locality, sharpness) : null);

    public override void SetVibration(float lowFrequency, float highFrequency)
    {
        _low.Run(_hasMotorsApart ? lowFrequency : Math.Max(lowFrequency, highFrequency));
        _high.Run(highFrequency);
    }

    public override void SetTriggerVibration(float left, float right)
    {
        _leftTrigger.Run(left);
        _rightTrigger.Run(right);
    }

    internal IntPtr Controller => _controller;

    public override string Name { get; }

    public override GamepadFace Face { get; }

    public override GamepadButton SupportedButtons => Ask(SupportedBy);

    public override GamepadState GetState() => Ask(Read);

    internal void Tick()
    {
        _low.Tick();
        _high.Tick();
        _leftTrigger.Tick();
        _rightTrigger.Tick();
    }

    internal void Disconnect()
    {
        IntPtr controller;
        lock (_lock)
        {
            controller = _controller;
            _controller = IntPtr.Zero;
        }

        if (controller == IntPtr.Zero)
            return;

        // A motor may still be starting on it: the controller goes when the last is let go of.
        Task.WhenAll(_low.Close(), _high.Close(), _leftTrigger.Close(), _rightTrigger.Close())
            .ContinueWith(_ => NSObject.Release(controller), TaskScheduler.Default);
    }

    private T Ask<T>(Func<IntPtr, T> read)
    {
        lock (_lock)
        {
            if (_controller == IntPtr.Zero)
                return default;

            var pool = ObjCRuntime.PushAutoreleasePool();
            try
            {
                var gamepad = GCController.GamepadOf(_controller);
                return gamepad == IntPtr.Zero ? default : read(gamepad);
            }
            finally
            {
                ObjCRuntime.PopAutoreleasePool(pool);
            }
        }
    }

    // The Elite's paddles come and go with its profile.
    private GamepadButton SupportedBy(IntPtr gamepad)
    {
        var supported = GamepadButton.DpadUp | GamepadButton.DpadDown | GamepadButton.DpadLeft | GamepadButton.DpadRight;
        foreach (var (button, selector) in _buttons)
        {
            if (GCController.PartOf(gamepad, selector) != IntPtr.Zero)
                supported |= button;
        }

        return supported;
    }

    private GamepadState Read(IntPtr gamepad)
    {
        var buttons = GamepadButton.None;
        foreach (var (button, selector) in _buttons)
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
