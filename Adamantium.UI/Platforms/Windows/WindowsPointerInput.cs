using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.Win32;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>Reads a WM_POINTER message about a finger or a pen into the platform-neutral raw event. Which button a
/// contact presses is not said here: <see cref="PointerDevice"/> decides that for every platform alike.</summary>
internal static class WindowsPointerInput
{
    private const float FullPressure = 1024f;

    /// <summary>The raw event of a finger or a pen, or null for a pointer the mouse messages carry - the mouse and a
    /// touchpad, which go on as WM_MOUSE*.</summary>
    public static RawPointerEventArgs Read(WindowMessages message, IntPtr wParam, IWindow window, uint timestamp)
    {
        var id = Messages.GetPointerId(wParam);
        if (!Win32Interop.GetPointerType(id, out var type))
        {
            return null;
        }

        POINTER_INFO info;
        PointerPointProperties properties;
        switch (type)
        {
            case PointerInputType.Touch when Win32Interop.GetPointerTouchInfo(id, out var touch):
                info = touch.pointerInfo;
                properties = Touch(touch, window);
                break;
            case PointerInputType.Pen when Win32Interop.GetPointerPenInfo(id, out var pen):
                info = pen.pointerInfo;
                properties = Pen(pen);
                break;
            default:
                return null;
        }

        var at = window.ScreenToClient(new PixelPoint(info.ptPixelLocation.X, info.ptPixelLocation.Y));
        return new RawPointerEventArgs(EventType(message, info.pointerFlags), (IInputComponent)window, at,
            KeyboardDevice.CurrentDevice.Modifiers, timestamp)
        {
            PointerId = id,
            PointerType = type == PointerInputType.Touch ? PointerType.Touch : PointerType.Pen,
            IsPrimary = (info.pointerFlags & PointerFlags.Primary) != 0,
            Properties = properties
        };
    }

    // A contact the system took away mid-gesture (an edge swipe, a palm) comes as an Up flagged canceled, or as the
    // window losing the pointer's capture.
    private static RawPointerEventType EventType(WindowMessages message, PointerFlags flags)
    {
        if ((flags & PointerFlags.Canceled) != 0)
        {
            return RawPointerEventType.Cancel;
        }

        return message switch
        {
            WindowMessages.PointerDown => RawPointerEventType.Down,
            WindowMessages.PointerUp => RawPointerEventType.Up,
            WindowMessages.PointerLeave => RawPointerEventType.LeaveWindow,
            WindowMessages.PointerCaptureChanged => RawPointerEventType.Cancel,
            _ => RawPointerEventType.Move
        };
    }

    private static PointerPointProperties Touch(POINTER_TOUCH_INFO touch, IWindow window)
    {
        var contact = default(Rect);
        if ((touch.touchMask & TouchMask.ContactArea) != 0)
        {
            var topLeft = window.ScreenToClient(new PixelPoint(touch.rcContact.Left, touch.rcContact.Top));
            var bottomRight = window.ScreenToClient(new PixelPoint(touch.rcContact.Right, touch.rcContact.Bottom));
            contact = new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
        }

        return new PointerPointProperties
        {
            IsLeftButtonPressed = (touch.pointerInfo.pointerFlags & PointerFlags.InContact) != 0,
            Pressure = (touch.touchMask & TouchMask.Pressure) != 0
                ? touch.pressure / FullPressure
                : PointerPointProperties.DefaultPressure,
            ContactRect = contact
        };
    }

    private static PointerPointProperties Pen(POINTER_PEN_INFO pen) => new()
    {
        IsLeftButtonPressed = (pen.pointerInfo.pointerFlags & PointerFlags.InContact) != 0,
        Pressure = (pen.penMask & PenMask.Pressure) != 0 ? pen.pressure / FullPressure : PointerPointProperties.DefaultPressure,
        XTilt = (pen.penMask & PenMask.TiltX) != 0 ? pen.tiltX : 0,
        YTilt = (pen.penMask & PenMask.TiltY) != 0 ? pen.tiltY : 0,
        Twist = (pen.penMask & PenMask.Rotation) != 0 ? pen.rotation : 0,
        IsEraser = (pen.penFlags & (PenFlags.Eraser | PenFlags.Inverted)) != 0,
        IsBarrelButtonPressed = (pen.penFlags & PenFlags.Barrel) != 0
    };
}
