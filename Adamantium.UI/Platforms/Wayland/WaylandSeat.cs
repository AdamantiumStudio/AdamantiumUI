using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.Wayland;
using KeyState = Adamantium.UI.Core.Input.KeyState;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>
/// The seat: the pointer and the keyboard, and the live answers the framework asks the platform for - where the
/// pointer is, what is held, which cursor shows, what the user set. Its events arrive on the thread running
/// <see cref="WaylandPlatform.Run"/> and go on to the loop thread as raw input of the window they are in.
/// <para>
/// Wayland repeats no key: the client does, at the rate wl_keyboard.repeat_info gives, timed by the platform's wait.
/// </para>
/// </summary>
internal sealed class WaylandSeat : INativeMouse, INativeKeyboard, INativePlatformSettings, INativeCursors
{
    // linux/input-event-codes.h
    private const uint ButtonLeft = 0x110;
    private const uint ButtonRight = 0x111;
    private const uint ButtonMiddle = 0x112;
    private const uint ButtonSide = 0x113;
    private const uint ButtonExtra = 0x114;

    // A wheel notch, as wl_pointer.axis_value120 and Windows both count it.
    private const int Notch = 120;

    // A touchpad's continuous scroll, in surface pixels, taken as a notch every this many.
    private const double PixelsPerNotch = 15;

    private readonly WaylandPlatform platform;
    private readonly WlSeat seat;
    private WlPointer pointer;
    private WlKeyboard keyboard;
    private WpCursorShapeDeviceV1 cursorShape;

    private WaylandWindowWorker pointerWindow;
    private WaylandWindowWorker keyboardWindow;
    private uint pointerEnterSerial;
    private Vector2 pointerPosition;
    private InputModifiers buttons;
    private WpCursorShapeDeviceV1.Shape appliedShape;
    private bool cursorHidden;

    private int wheel120Vertical;
    private int wheel120Horizontal;
    private double wheelPixelsVertical;
    private double wheelPixelsHorizontal;
    private uint wheelTime;

    private XkbKeyboard xkb;
    private readonly HashSet<Key> keysDown = new();
    private int repeatRate = 25;
    private int repeatDelay = 600;
    private uint repeatCode;
    private long repeatDue = long.MaxValue;

    public WaylandSeat(WaylandPlatform platform, WlSeat seat)
    {
        this.platform = platform;
        this.seat = seat;
        if (seat != null)
        {
            seat.Capabilities += OnCapabilities;
        }
    }

    public WlSeat Proxy => seat;

    /// <summary>The serial of the last press or key: what a move or a resize of a window is asked for with.</summary>
    public uint LastInputSerial { get; private set; }

    #region INativeMouse, INativeKeyboard, INativePlatformSettings

    // The desktop is the window's own client area under Wayland, in physical pixels like a Win32 desktop point.
    public PixelPoint Position
    {
        get
        {
            var scale = pointerWindow?.Scale ?? 1.0;
            return new PixelPoint(pointerPosition.X * scale, pointerPosition.Y * scale);
        }
        // A client does not warp the pointer on Wayland.
        set { }
    }

    public bool IsKeyDown(Key key)
    {
        lock (keysDown)
        {
            return keysDown.Contains(key);
        }
    }

    public bool IsKeyToggled(Key key)
    {
        var modifiers = xkb?.Modifiers ?? XkbModifiers.None;
        return key switch
        {
            Key.CapsLock => (modifiers & XkbModifiers.CapsLock) != 0,
            Key.NumLock => (modifiers & XkbModifiers.NumLock) != 0,
            _ => false,
        };
    }

    // GTK's defaults: the desktop's own settings live in gsettings, which a client of the compositor does not read.
    public uint DoubleClickTime => 400;

    public Size DoubleClickSize => new(4, 4);

    public uint HoverTime => 500;

    public Size DragThreshold => new(8, 8);

    public Rect VirtualScreen => default;

    public IReadOnlyList<ScreenInfo> Screens => Array.Empty<ScreenInfo>();

    #endregion

    #region INativeCursors

    /// <summary>Shows the cursor through wp_cursor_shape: the compositor draws it from the user's theme. Without the
    /// protocol the compositor's own arrow stays.</summary>
    public void Apply(Cursor cursor)
    {
        if (cursor == null || cursor.Type == CursorType.None)
        {
            HideCursor();
            return;
        }

        SetShape(ShapeOf(cursor.Type));
    }

    internal void SetShape(WpCursorShapeDeviceV1.Shape shape)
    {
        var device = cursorShape;
        if (device == null || pointerWindow == null || (!cursorHidden && appliedShape == shape))
        {
            return;
        }

        device.SetShape(pointerEnterSerial, shape);
        appliedShape = shape;
        cursorHidden = false;
    }

    private void HideCursor()
    {
        if (pointer == null || pointerWindow == null || cursorHidden)
        {
            return;
        }

        pointer.SetCursor(pointerEnterSerial, null, 0, 0);
        cursorHidden = true;
    }

    private static WpCursorShapeDeviceV1.Shape ShapeOf(CursorType type) => type switch
    {
        CursorType.AppStarting => WpCursorShapeDeviceV1.Shape.Progress,
        CursorType.Crosshair => WpCursorShapeDeviceV1.Shape.Crosshair,
        CursorType.Hand => WpCursorShapeDeviceV1.Shape.Pointer,
        CursorType.Help => WpCursorShapeDeviceV1.Shape.Help,
        CursorType.IBeam => WpCursorShapeDeviceV1.Shape.Text,
        CursorType.No => WpCursorShapeDeviceV1.Shape.NotAllowed,
        CursorType.SizeAll => WpCursorShapeDeviceV1.Shape.Move,
        CursorType.SizeNESW => WpCursorShapeDeviceV1.Shape.NeswResize,
        CursorType.SizeNS => WpCursorShapeDeviceV1.Shape.NsResize,
        CursorType.SizeNWSE => WpCursorShapeDeviceV1.Shape.NwseResize,
        CursorType.SizeEWE => WpCursorShapeDeviceV1.Shape.EwResize,
        CursorType.Wait => WpCursorShapeDeviceV1.Shape.Wait,
        CursorType.DragCopy => WpCursorShapeDeviceV1.Shape.Copy,
        CursorType.DragLink => WpCursorShapeDeviceV1.Shape.Alias,
        _ => WpCursorShapeDeviceV1.Shape.Default,
    };

    #endregion

    #region Devices

    private void OnCapabilities(WlSeat.Capability capabilities)
    {
        var hasPointer = (capabilities & WlSeat.Capability.Pointer) != 0;
        if (hasPointer && pointer == null)
        {
            pointer = seat.GetPointer();
            pointer.Enter += OnPointerEnter;
            pointer.Leave += OnPointerLeave;
            pointer.Motion += OnPointerMotion;
            pointer.Button += OnPointerButton;
            pointer.AxisEvent += OnAxis;
            pointer.AxisValue120 += OnAxisValue120;
            pointer.Frame += OnPointerFrame;
            cursorShape = platform.CursorShapes?.GetPointer(pointer);
        }
        else if (!hasPointer && pointer != null)
        {
            cursorShape?.Destroy();
            cursorShape = null;
            pointer.Release();
            pointer = null;
            pointerWindow = null;
        }

        var hasKeyboard = (capabilities & WlSeat.Capability.Keyboard) != 0;
        if (hasKeyboard && keyboard == null)
        {
            keyboard = seat.GetKeyboard();
            keyboard.Keymap += OnKeymap;
            keyboard.Enter += OnKeyboardEnter;
            keyboard.Leave += OnKeyboardLeave;
            keyboard.Key += OnKey;
            keyboard.Modifiers += OnModifiers;
            keyboard.RepeatInfo += OnRepeatInfo;
        }
        else if (!hasKeyboard && keyboard != null)
        {
            keyboard.Release();
            keyboard = null;
            keyboardWindow = null;
            StopRepeat();
        }
    }

    #endregion

    #region Pointer

    private void OnPointerEnter(uint serial, WlSurface surface, double x, double y)
    {
        pointerWindow = platform.Find(surface);
        pointerEnterSerial = serial;
        pointerPosition = new Vector2(x, y);

        // The cursor belongs to the surface it was set for: a new entry starts from nothing.
        appliedShape = 0;
        cursorHidden = false;
        PointerMoved(0);
    }

    private void OnPointerLeave(uint serial, WlSurface surface)
    {
        var window = pointerWindow?.Window;
        pointerWindow = null;
        if (window == null)
        {
            return;
        }

        var args = new RawPointerEventArgs(RawPointerEventType.LeaveWindow, window, Vector2.Zero, InputModifiers(), 0);
        WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
    }

    private void OnPointerMotion(uint time, double x, double y)
    {
        pointerPosition = new Vector2(x, y);
        PointerMoved(time);
    }

    private void PointerMoved(uint time)
    {
        var worker = pointerWindow;
        if (worker?.Window == null)
        {
            return;
        }

        // On an edge the window is resized from, the cursor says so; anywhere else it is what the content asks for.
        var edge = worker.ResizeEdgeAt(pointerPosition);
        if (edge != XdgToplevel.ResizeEdge.None)
        {
            SetShape(ShapeOf(edge));
        }
        else
        {
            Apply(Mouse.OverrideCursor ?? Mouse.Cursor);
        }

        var args = new RawPointerEventArgs(RawPointerEventType.Move, worker.Window, pointerPosition, InputModifiers(), time);
        WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
    }

    private void OnPointerButton(uint serial, uint time, uint button, WlPointer.ButtonState state)
    {
        LastInputSerial = serial;
        var worker = pointerWindow;
        var changed = ButtonOf(button);
        var pressed = state == WlPointer.ButtonState.Pressed;

        // A press on an edge of a window drawing its own frame is the compositor's resize, not the content's click.
        if (pressed && changed == MouseButtons.Left && worker != null &&
            worker.BeginResize(worker.ResizeEdgeAt(pointerPosition), serial))
        {
            return;
        }

        var flag = FlagOf(changed);
        buttons = pressed ? buttons | flag : buttons & ~flag;

        if (worker?.Window == null || changed == MouseButtons.None)
        {
            return;
        }

        var args = new RawPointerEventArgs(pressed ? RawPointerEventType.Down : RawPointerEventType.Up, worker.Window,
            pointerPosition, InputModifiers(), time) { ChangedButton = changed };
        WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
    }

    private void OnAxis(uint time, WlPointer.Axis axis, double value)
    {
        wheelTime = time;
        if (axis == WlPointer.Axis.VerticalScroll) wheelPixelsVertical += value;
        else wheelPixelsHorizontal += value;
    }

    private void OnAxisValue120(WlPointer.Axis axis, int value120)
    {
        if (axis == WlPointer.Axis.VerticalScroll) wheel120Vertical += value120;
        else wheel120Horizontal += value120;
    }

    // A frame closes one gesture's worth of axis events: a wheel's notches when there are any, the pixels of a
    // touchpad's scroll otherwise. Down is positive on Wayland and negative on Windows; right is positive on both.
    private void OnPointerFrame()
    {
        var vertical = wheel120Vertical != 0 ? wheel120Vertical : (int)Math.Round(wheelPixelsVertical / PixelsPerNotch * Notch);
        var horizontal = wheel120Horizontal != 0 ? wheel120Horizontal : (int)Math.Round(wheelPixelsHorizontal / PixelsPerNotch * Notch);
        wheel120Vertical = wheel120Horizontal = 0;
        wheelPixelsVertical = wheelPixelsHorizontal = 0;

        var window = pointerWindow?.Window;
        if (window == null)
        {
            return;
        }

        if (vertical != 0)
        {
            DispatchWheel(window, -vertical, false);
        }

        if (horizontal != 0)
        {
            DispatchWheel(window, horizontal, true);
        }
    }

    private void DispatchWheel(IWindow window, int delta, bool horizontal)
    {
        var args = new RawPointerWheelEventArgs(delta, window, pointerPosition, InputModifiers(), wheelTime, horizontal);
        WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
    }

    private InputModifiers InputModifiers() => KeyboardDevice.CurrentDevice.Modifiers | buttons;

    private static MouseButtons ButtonOf(uint button) => button switch
    {
        ButtonLeft => MouseButtons.Left,
        ButtonRight => MouseButtons.Right,
        ButtonMiddle => MouseButtons.Middle,
        ButtonSide => MouseButtons.XButton1,
        ButtonExtra => MouseButtons.XButton2,
        _ => MouseButtons.None,
    };

    private static InputModifiers FlagOf(MouseButtons button) => button switch
    {
        MouseButtons.Left => Core.Input.InputModifiers.LeftMouseButton,
        MouseButtons.Right => Core.Input.InputModifiers.RightMouseButton,
        MouseButtons.Middle => Core.Input.InputModifiers.MiddleMouseButton,
        MouseButtons.XButton1 => Core.Input.InputModifiers.X1MouseButton,
        MouseButtons.XButton2 => Core.Input.InputModifiers.X2MouseButton,
        _ => Core.Input.InputModifiers.None,
    };

    private static WpCursorShapeDeviceV1.Shape ShapeOf(XdgToplevel.ResizeEdge edge) => edge switch
    {
        XdgToplevel.ResizeEdge.Top or XdgToplevel.ResizeEdge.Bottom => WpCursorShapeDeviceV1.Shape.NsResize,
        XdgToplevel.ResizeEdge.Left or XdgToplevel.ResizeEdge.Right => WpCursorShapeDeviceV1.Shape.EwResize,
        XdgToplevel.ResizeEdge.TopLeft or XdgToplevel.ResizeEdge.BottomRight => WpCursorShapeDeviceV1.Shape.NwseResize,
        _ => WpCursorShapeDeviceV1.Shape.NeswResize,
    };

    #endregion

    #region Keyboard

    private void OnKeymap(WlKeyboard.KeymapFormat format, int fd, uint size)
    {
        if (format != WlKeyboard.KeymapFormat.XkbV1)
        {
            LinuxInterop.close(fd);
            return;
        }

        xkb?.Dispose();
        xkb = XkbKeyboard.FromKeymap(fd, size);
    }

    private void OnKeyboardEnter(uint serial, WlSurface surface, ReadOnlySpan<byte> keys)
    {
        keyboardWindow = platform.Find(surface);
    }

    // What was held is let go of with the focus: the keys come up somewhere this window does not hear.
    private void OnKeyboardLeave(uint serial, WlSurface surface)
    {
        keyboardWindow = null;
        StopRepeat();
        lock (keysDown)
        {
            keysDown.Clear();
        }
    }

    private void OnModifiers(uint serial, uint depressed, uint latched, uint locked, uint group)
    {
        xkb?.UpdateModifiers(depressed, latched, locked, group);
    }

    private void OnRepeatInfo(int rate, int delay)
    {
        repeatRate = rate;
        repeatDelay = delay;
    }

    private void OnKey(uint serial, uint time, uint code, WlKeyboard.KeyState state)
    {
        LastInputSerial = serial;
        if (xkb == null)
        {
            return;
        }

        var key = KeyOf(code);
        if (state == WlKeyboard.KeyState.Pressed)
        {
            lock (keysDown)
            {
                keysDown.Add(key);
            }

            Press(code, key, time, repeated: false);

            // A rate of zero is the user's "no repeat".
            if (xkb.Repeats(code) && repeatRate > 0)
            {
                repeatCode = code;
                repeatDue = Environment.TickCount64 + repeatDelay;
            }
        }
        else
        {
            lock (keysDown)
            {
                keysDown.Remove(key);
            }

            if (code == repeatCode)
            {
                StopRepeat();
            }

            var window = keyboardWindow?.Window;
            if (window != null)
            {
                var press = new KeyPressInfo
                {
                    PreviousState = KeyState.Down, CurrentState = KeyState.Up, RepeatCount = 1,
                    PhysicalKey = WaylandKeys.ToHidUsage(code), PressTime = time,
                };
                var modifiers = KeyboardDevice.CurrentDevice.Modifiers;
                WaylandPlatform.DispatchInput(() => KeyboardDevice.CurrentDevice.ProcessEvent(
                    new RawKeyboardEventArgs(key, RawKeyboardEventType.KeyUp, press, modifiers, time), window));
            }
        }
    }

    private Key KeyOf(uint code) =>
        WaylandKeys.ToKey(code, xkb.KeySym(code), (xkb.Modifiers & XkbModifiers.NumLock) != 0);

    private void Press(uint code, Key key, uint time, bool repeated)
    {
        // ...to THIS window when nothing in it is focused, as on Windows: see KeyboardDevice.ProcessEvent.
        var window = keyboardWindow?.Window;
        if (window == null)
        {
            return;
        }

        var press = new KeyPressInfo
        {
            PreviousState = repeated ? KeyState.Down : KeyState.Up, CurrentState = KeyState.Down, RepeatCount = 1,
            PhysicalKey = WaylandKeys.ToHidUsage(code), PressTime = time,
        };
        var modifiers = KeyboardDevice.CurrentDevice.Modifiers;
        WaylandPlatform.DispatchInput(() => KeyboardDevice.CurrentDevice.ProcessEvent(
            new RawKeyboardEventArgs(key, RawKeyboardEventType.KeyDown, press, modifiers, time), window));

        // Text is what the key types - unless Ctrl or Alt hold it, which make it a shortcut: Windows sends no WM_CHAR
        // for those either. AltGr is not Alt here; what it types is text.
        var held = xkb.Modifiers;
        if ((held & (XkbModifiers.Control | XkbModifiers.Alt)) != 0)
        {
            return;
        }

        var text = xkb.Text(code);
        if (text.Length > 0 && text[0] >= 32 && text[0] != 127)
        {
            WaylandPlatform.DispatchInput(() => KeyboardDevice.CurrentDevice.ProcessEvent(
                new RawTextInputEventArgs(text, modifiers, time)));
        }
    }

    /// <summary>How long the platform's wait may last before the held key repeats; -1 when none is held.</summary>
    public int RepeatTimeout()
    {
        if (repeatDue == long.MaxValue)
        {
            return -1;
        }

        return (int)Math.Clamp(repeatDue - Environment.TickCount64, 0, int.MaxValue);
    }

    /// <summary>Repeats the held key if its time has come.</summary>
    public void Repeat()
    {
        if (repeatDue == long.MaxValue || Environment.TickCount64 < repeatDue || xkb == null)
        {
            return;
        }

        Press(repeatCode, KeyOf(repeatCode), (uint)Environment.TickCount64, repeated: true);
        repeatDue += 1000 / repeatRate;

        // A wait that overslept does not repeat the key a burst of times to catch up.
        if (repeatDue < Environment.TickCount64)
        {
            repeatDue = Environment.TickCount64 + 1000 / repeatRate;
        }
    }

    private void StopRepeat()
    {
        repeatDue = long.MaxValue;
        repeatCode = 0;
    }

    #endregion
}
