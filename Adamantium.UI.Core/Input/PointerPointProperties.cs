namespace Adamantium.UI.Core.Input;

/// <summary>What a pointer reports beside its position. A finger in contact and a pen tip on the surface press the left
/// button; a pen's barrel button presses the right one.</summary>
public readonly record struct PointerPointProperties
{
    /// <summary>The pressure a mouse reports: halfway, so a stroke drawn with it has the width a pen pressing normally
    /// would give.</summary>
    public const float DefaultPressure = 0.5f;

    public bool IsLeftButtonPressed { get; init; }

    public bool IsRightButtonPressed { get; init; }

    public bool IsMiddleButtonPressed { get; init; }

    public bool IsXButton1Pressed { get; init; }

    public bool IsXButton2Pressed { get; init; }

    /// <summary>How hard the pen or finger presses, from 0 to 1.</summary>
    public float Pressure { get; init; }

    /// <summary>The pen's tilt from the vertical toward +X, in degrees from -90 to 90.</summary>
    public float XTilt { get; init; }

    /// <summary>The pen's tilt from the vertical toward +Y, in degrees from -90 to 90.</summary>
    public float YTilt { get; init; }

    /// <summary>The pen's rotation about its own axis, in degrees from 0 to 359.</summary>
    public float Twist { get; init; }

    /// <summary>True while the pen's eraser end is in use.</summary>
    public bool IsEraser { get; init; }

    /// <summary>True while the pen's barrel button is held.</summary>
    public bool IsBarrelButtonPressed { get; init; }

    /// <summary>The area a finger touches, in the element's units; empty for the mouse and the pen.</summary>
    public Rect ContactRect { get; init; }

    /// <summary>Which buttons are down.</summary>
    public MouseButtons Buttons =>
        (IsLeftButtonPressed ? MouseButtons.Left : MouseButtons.None) |
        (IsRightButtonPressed ? MouseButtons.Right : MouseButtons.None) |
        (IsMiddleButtonPressed ? MouseButtons.Middle : MouseButtons.None) |
        (IsXButton1Pressed ? MouseButtons.XButton1 : MouseButtons.None) |
        (IsXButton2Pressed ? MouseButtons.XButton2 : MouseButtons.None);

    /// <summary>The mouse's properties from the buttons a mouse event carries.</summary>
    public static PointerPointProperties FromMouse(InputModifiers modifiers) => new()
    {
        IsLeftButtonPressed = (modifiers & InputModifiers.LeftMouseButton) != 0,
        IsRightButtonPressed = (modifiers & InputModifiers.RightMouseButton) != 0,
        IsMiddleButtonPressed = (modifiers & InputModifiers.MiddleMouseButton) != 0,
        IsXButton1Pressed = (modifiers & InputModifiers.X1MouseButton) != 0,
        IsXButton2Pressed = (modifiers & InputModifiers.X2MouseButton) != 0,
        Pressure = DefaultPressure
    };

    /// <summary>Whether <paramref name="button"/> is down.</summary>
    public bool IsPressed(MouseButtons button) => (Buttons & button) != 0;
}
