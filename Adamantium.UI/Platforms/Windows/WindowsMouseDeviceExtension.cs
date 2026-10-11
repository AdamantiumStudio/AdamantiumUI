using System;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.Win32;

namespace Adamantium.UI.Platforms.Windows;

public static class WindowsMouseDeviceExtension
{
    public static InputModifiers GetKeyModifiers(this WindowMessages msg, IntPtr wParam)
    {
        var mouseButtons = Messages.GetMouseModifyKeys(msg, wParam);
        var modifiers = KeyboardDevice.CurrentDevice.Modifiers;
        if (mouseButtons.HasFlag(MouseModifiers.LeftButton))
        {
            modifiers |= InputModifiers.LeftMouseButton;
        }
        if (mouseButtons.HasFlag(MouseModifiers.RightButton))
        {
            modifiers |= InputModifiers.RightMouseButton;
        }
        if (mouseButtons.HasFlag(MouseModifiers.MiddleButton))
        {
            modifiers |= InputModifiers.MiddleMouseButton;
        }
        if (mouseButtons.HasFlag(MouseModifiers.XButton1))
        {
            modifiers |= InputModifiers.X1MouseButton;
        }
        if (mouseButtons.HasFlag(MouseModifiers.XButton2))
        {
            modifiers |= InputModifiers.X2MouseButton;
        }
        return modifiers;
    }

    /// <summary>Translate a Win32 mouse message into the neutral event type and the button it changed. EXPLICITLY, every
    /// case: this used to end in a cast of the message, which only worked because the enum's values WERE the message
    /// numbers - a bargain that quietly made a platform-neutral type Windows-shaped.</summary>
    public static RawPointerEventType EventTypeFromMessage(this WindowMessages msg, IntPtr wParam, out MouseButtons button)
    {
        button = MouseButtons.None;
        switch (msg)
        {
            // A CS_DBLCLKS window delivers the SECOND press of a double-click as WM_*BUTTONDBLCLK (not a plain DOWN). The
            // device layer has no separate double-click event - it recomputes the click count from timing - so surface a
            // dbl-click as a normal button DOWN. Without this the second press was dropped, so ClickCount never reached 2
            // (double-click-to-maximize, "select word", etc. never fired) and double-clicking a control produced one click.
            case WindowMessages.LeftButtondown:
            case WindowMessages.LeftButtondblclk:
                button = MouseButtons.Left;
                return RawPointerEventType.Down;
            case WindowMessages.LeftButtonup:
                button = MouseButtons.Left;
                return RawPointerEventType.Up;
            case WindowMessages.RightButtondown:
            case WindowMessages.RightButtondblclk:
                button = MouseButtons.Right;
                return RawPointerEventType.Down;
            case WindowMessages.RightButtonup:
                button = MouseButtons.Right;
                return RawPointerEventType.Up;
            case WindowMessages.MiddleButtondown:
            case WindowMessages.MiddleButtondblclk:
                button = MouseButtons.Middle;
                return RawPointerEventType.Down;
            case WindowMessages.MiddleButtonup:
                button = MouseButtons.Middle;
                return RawPointerEventType.Up;
            case WindowMessages.Xbuttondown:
            case WindowMessages.Xbuttondblclk:
                button = XButton(msg, wParam);
                return RawPointerEventType.Down;
            case WindowMessages.Xbuttonup:
                button = XButton(msg, wParam);
                return RawPointerEventType.Up;
            case WindowMessages.Mouseleave:
                return RawPointerEventType.LeaveWindow;
            case WindowMessages.MouseWheel:
            case WindowMessages.MouseHwheel:
                return RawPointerEventType.Wheel;
            default:
                return RawPointerEventType.Move;
        }
    }

    private static MouseButtons XButton(WindowMessages msg, IntPtr wParam) =>
        Messages.GetMouseModifyKeys(msg, wParam) == MouseModifiers.XButton1 ? MouseButtons.XButton1 : MouseButtons.XButton2;
}
