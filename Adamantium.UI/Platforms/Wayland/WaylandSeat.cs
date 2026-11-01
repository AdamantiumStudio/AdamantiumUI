using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Adamantium.Core;
using Adamantium.Fonts;
using Adamantium.Fonts.Text;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.Wayland;
using KeyState = Adamantium.UI.Core.Input.KeyState;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The seat's pointer and keyboard, as raw input of the window they are in; key repeat is timed here.</summary>
internal sealed class WaylandSeat : INativeMouse, INativeKeyboard, INativePlatformSettings, INativeCursors
{
    private const uint ButtonLeft = 0x110;
    private const uint ButtonRight = 0x111;
    private const uint ButtonMiddle = 0x112;
    private const uint ButtonSide = 0x113;
    private const uint ButtonExtra = 0x114;

    private const int Notch = 120;

    private const double PixelsPerNotch = 15;

    private readonly WaylandPlatform platform;
    private readonly WlSeat seat;
    private WlPointer pointer;
    private WlKeyboard keyboard;
    private volatile WaylandTouch touch;
    private volatile bool touchSuppressed;
    private WaylandPointerSnapshot pointerSnapshot = new(null, Vector2<double>.Zero, 0);
    private uint lastPointerId = PointerDevice.MouseId;
    private WpCursorShapeDeviceV1 cursorShape;

    private WaylandWindowWorker pointerWindow;
    private WaylandWindowWorker keyboardWindow;
    private uint pointerEnterSerial;
    private Vector2<double> pointerPosition;
    private InputModifiers buttons;
    private WpCursorShapeDeviceV1.Shape appliedShape;
    private string appliedImage;
    private Cursor appliedCursor;
    private WlSurface cursorSurface;
    private WpViewport cursorViewport;
    private readonly Dictionary<(string, int), WaylandCursorImage> cursorImages = new();
    private bool cursorHidden;
    private ZwpPointerGesturePinchV1 pinch;
    private double pinchScale;
    private double pinchNotches;
    private ZwpRelativePointerV1 relativePointer;
    private ZwpLockedPointerV1 lockedPointer;
    private WaylandWindowWorker lockedWindow;

    private int wheel120Vertical;
    private int wheel120Horizontal;
    private double wheelPixelsVertical;
    private double wheelPixelsHorizontal;
    private uint wheelTime;

    private static readonly (uint First, uint Last)[] EvdevLetterRows = [(16, 25), (30, 38), (44, 50)];

    private XkbKeyboard xkb;
    private volatile bool hasRightToLeftLayout;
    private readonly HashSet<Key> keysDown = new();
    private readonly object _keysDownLock = new();
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

    /// <summary>The window <see cref="LastInputSerial"/> came in on: a compositor checks the serial against it.</summary>
    internal WaylandWindowWorker LastInputWindow { get; private set; }

    /// <summary>Takes a press or a key, of any device, as the input a move, a resize or an activation of a window is
    /// asked with.</summary>
    internal void NoteInput(uint serial, WaylandWindowWorker window)
    {
        LastInputSerial = serial;
        LastInputWindow = window;
    }

    /// <summary>An id no pointer of the framework has: each finger and pen contact is a pointer of its own.</summary>
    internal uint NextPointerId() => ++lastPointerId;

    /// <summary>The serial of the last pointer button press: what a drag is started with.</summary>
    internal uint PressSerial { get; private set; }

    /// <summary>Whether a pointer button is held.</summary>
    internal bool IsPressed => buttons != default;

    /// <summary>The keys and buttons held now.</summary>
    internal InputModifiers Modifiers => InputModifiers();

    /// <summary>The pointer entered, moved over or left one of our windows: <see cref="Pointer"/> says where it is now.</summary>
    internal event Action PointerChanged;

    /// <summary>A pointer button went up over one of our windows.</summary>
    internal event Action PointerReleased;

    /// <summary>The window the pointer is in.</summary>
    internal WaylandWindowWorker PointerWindow => pointerWindow;

    /// <summary>The window the keyboard is in.</summary>
    internal WaylandWindowWorker KeyboardWindow => keyboardWindow;

    /// <summary>While set, fingers that go down are not raised: the system's own warm-up touches.</summary>
    internal bool TouchSuppressed
    {
        get => touchSuppressed;
        set => touchSuppressed = value;
    }

    /// <summary>Whether the seat has a touchscreen.</summary>
    internal bool HasTouch => touch != null;

    /// <summary>Raises the input still waiting for the frame that closes it, once the events read are dispatched.</summary>
    internal void Flush() => touch?.Flush();

    /// <summary>The pointer at its last event, readable from any thread.</summary>
    internal WaylandPointerSnapshot Pointer => Volatile.Read(ref pointerSnapshot);

    #region INativeMouse, INativeKeyboard, INativePlatformSettings

    public PixelPoint Position
    {
        get
        {
            var scale = pointerWindow?.Scale ?? 1.0;
            return new PixelPoint(pointerPosition.X * scale, pointerPosition.Y * scale);
        }

        set { }
    }

    public bool IsKeyDown(Key key)
    {
        lock (_keysDownLock)
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

    public bool HasRightToLeftLayout => hasRightToLeftLayout;

    public uint DoubleClickTime => 400;

    public Size<double> DoubleClickSize => new(4, 4);

    public uint HoverTime => 500;

    public Size<double> DragThreshold => new(8, 8);

    #endregion

    #region INativeCursors

    /// <summary>Shows the cursor through wp_cursor_shape: the compositor draws it from the user's theme. Without the
    /// protocol the theme's picture of the shape is shown, read here: a compositor shows no cursor over a window that
    /// sets none.</summary>
    public void Apply(Cursor cursor)
    {
        appliedCursor = cursor;
        if (cursor == null || cursor.Type == CursorType.None)
        {
            HideCursor();
            return;
        }

        if (cursor.Type == CursorType.Custom && cursor.FilePath is { Length: > 0 } path)
        {
            SetImage(path, WaylandCursorImage.LogicalSize);
            return;
        }

        SetShape(ShapeOf(cursor.Type));
    }

    internal void SetShape(WpCursorShapeDeviceV1.Shape shape)
    {
        var device = cursorShape;
        if (device == null)
        {
            if (WaylandCursorTheme.Find(shape) is { } themed)
            {
                SetImage(themed, WaylandCursorTheme.Size);
            }

            return;
        }

        if (pointerWindow == null || (!cursorHidden && appliedShape == shape))
        {
            return;
        }

        device.SetShape(pointerEnterSerial, shape);
        appliedShape = shape;
        appliedImage = null;
        cursorHidden = false;
    }

    // Shows the picture in the file at logicalSize, its nominal size as the file has it, sharp at the window's scale.
    private void SetImage(string path, int logicalSize)
    {
        var worker = pointerWindow;
        if (pointer == null || worker == null || (!cursorHidden && appliedImage == path))
        {
            return;
        }

        var wanted = (int)Math.Round(logicalSize * worker.Scale);
        if (!cursorImages.TryGetValue((path, wanted), out var image))
        {
            image = WaylandCursorImage.Load(path, wanted);
            cursorImages[(path, wanted)] = image;
        }

        var buffer = image == null ? null : WaylandShm.CreateBuffer(platform, image.Pixels, image.Width, image.Height);
        if (buffer == null)
        {
            // The compositor's own default when it draws by shape; without that there is no other picture to fall to.
            if (cursorShape != null)
            {
                SetShape(WpCursorShapeDeviceV1.Shape.Default);
            }

            return;
        }

        cursorSurface ??= platform.Compositor.CreateSurface();
        double perPixel;
        if (platform.Viewporter != null)
        {
            cursorViewport ??= platform.Viewporter.GetViewport(cursorSurface);
            perPixel = (double)logicalSize / image.NominalSize;
            cursorViewport.SetDestination(Math.Max(1, (int)Math.Round(image.Width * perPixel)),
                Math.Max(1, (int)Math.Round(image.Height * perPixel)));
        }
        else
        {
            var bufferScale = Math.Max(1, (int)Math.Round((double)image.NominalSize / logicalSize));
            if (image.Width % bufferScale != 0 || image.Height % bufferScale != 0)
            {
                bufferScale = 1;
            }

            cursorSurface.SetBufferScale(bufferScale);
            perPixel = 1.0 / bufferScale;
        }

        // The cursor role first, then the picture: WSLg takes a surface committed with a picture and no role yet for a
        // window of its own, and never shows it as the cursor.
        pointer.SetCursor(pointerEnterSerial, cursorSurface, (int)Math.Round(image.HotX * perPixel),
            (int)Math.Round(image.HotY * perPixel));
        cursorSurface.Attach(buffer, 0, 0);
        cursorSurface.DamageBuffer(0, 0, image.Width, image.Height);
        cursorSurface.Commit();
        WaylandShm.DestroyWhenReleased(buffer);
        appliedShape = 0;
        appliedImage = path;
        cursorHidden = false;
    }

    /// <summary>Holds the pointer still in the window, hidden, and raises its moves as deltas until
    /// <see cref="UnlockPointer"/>.</summary>
    internal void LockPointer(WaylandWindowWorker worker)
    {
        if (pointer == null || platform.RelativePointers == null || platform.PointerConstraints == null ||
            lockedPointer != null || worker.Surface == null)
        {
            return;
        }

        if (relativePointer == null)
        {
            relativePointer = platform.RelativePointers.GetRelativePointer(pointer);
            relativePointer.RelativeMotion += OnRelativeMotion;
        }

        lockedWindow = worker;
        lockedPointer = platform.PointerConstraints.LockPointer(worker.Surface, pointer, null,
            ZwpPointerConstraintsV1.Lifetime.Persistent);
        HideCursor();
        platform.Display.Flush();
    }

    /// <summary>Lets the pointer go, showing it at <paramref name="at"/>, a logical point of the window it was held in.</summary>
    internal void UnlockPointer(Vector2<double> at)
    {
        if (lockedPointer == null)
        {
            return;
        }

        lockedPointer.SetCursorPositionHint(at.X, at.Y);
        lockedWindow.Surface.Commit();
        lockedPointer.Destroy();
        lockedPointer = null;
        lockedWindow = null;
        Apply(appliedCursor ?? Cursor.Default);
        platform.Display.Flush();
    }

    /// <summary>Drops what the seat holds of a window that is going away: its pointer lock and its last input.</summary>
    internal void Forget(WaylandWindowWorker worker)
    {
        if (LastInputWindow == worker)
        {
            LastInputWindow = null;
        }

        if (lockedWindow != worker || lockedPointer == null)
        {
            return;
        }

        lockedPointer.Destroy();
        lockedPointer = null;
        lockedWindow = null;
    }

    private void OnRelativeMotion(uint utimeHi, uint utimeLo, double dx, double dy, double dxUnaccel, double dyUnaccel)
    {
        var worker = lockedWindow;
        if (lockedPointer == null || worker?.InputWindow is not { } window)
        {
            return;
        }

        var time = (uint)((((ulong)utimeHi << 32) | utimeLo) / 1000);
        var args = new RawMouseDeltaEventArgs(new Vector2<double>(dx, dy) * worker.Scale, window, InputModifiers(), time);
        WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessDelta(args));
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
        CursorType.SizeWE => WpCursorShapeDeviceV1.Shape.EwResize,
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
            pinch = platform.PointerGestures?.GetPinchGesture(pointer);
            if (pinch != null)
            {
                pinch.Begin += (_, _, _, _) => (pinchScale, pinchNotches) = (1, 0);
                pinch.Update += OnPinchUpdate;
            }
        }
        else if (!hasPointer && pointer != null)
        {
            lockedPointer?.Destroy();
            lockedPointer = null;
            lockedWindow = null;
            relativePointer?.Destroy();
            relativePointer = null;
            pinch?.Destroy();
            pinch = null;
            cursorShape?.Destroy();
            cursorShape = null;
            pointer.Release();
            pointer = null;
            pointerWindow = null;
        }

        var hasTouch = (capabilities & WlSeat.Capability.Touch) != 0;
        if (hasTouch && touch == null)
        {
            touch = new WaylandTouch(platform, this, seat.GetTouch());
        }
        else if (!hasTouch && touch != null)
        {
            touch.Release();
            touch = null;
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
        pointerPosition = new Vector2<double>(x, y);

        appliedShape = 0;
        appliedImage = null;
        cursorHidden = false;
        PointerMoved(0);
    }

    private void OnPointerLeave(uint serial, WlSurface surface)
    {
        var window = pointerWindow?.Window;
        pointerWindow = null;
        Publish(null);
        buttons = default;
        if (window == null)
        {
            return;
        }

        var args = new RawPointerEventArgs(RawPointerEventType.LeaveWindow, window, Vector2<double>.Zero, InputModifiers(), 0);
        WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
    }

    private void Publish(WaylandWindowWorker worker)
    {
        var sequence = Volatile.Read(ref pointerSnapshot).Sequence + 1;
        Volatile.Write(ref pointerSnapshot, new WaylandPointerSnapshot(worker, pointerPosition, sequence));
        PointerChanged?.Invoke();
    }

    private void OnPointerMotion(uint time, double x, double y)
    {
        pointerPosition = new Vector2<double>(x, y);
        PointerMoved(time);
    }

    private void PointerMoved(uint time)
    {
        var worker = pointerWindow;
        Publish(worker);
        if (worker?.InputWindow is not { } window)
        {
            if (worker?.Window != null)
            {
                Apply(Cursor.Default);
            }

            return;
        }

        var edge = worker.ResizeEdgeAt(pointerPosition);
        if (edge != XdgToplevel.ResizeEdge.None)
        {
            SetShape(ShapeOf(edge));
        }
        else
        {
            Apply(Mouse.OverrideCursor ?? Mouse.Cursor);
        }

        var args = new RawPointerEventArgs(RawPointerEventType.Move, window, pointerPosition, InputModifiers(), time);
        WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
    }

    private void OnPointerButton(uint serial, uint time, uint button, WlPointer.ButtonState state)
    {
        NoteInput(serial, pointerWindow);
        var worker = pointerWindow;
        var changed = ButtonOf(button);
        var pressed = state == WlPointer.ButtonState.Pressed;

        if (pressed && changed == MouseButtons.Left && worker?.InputWindow != null &&
            worker.BeginResize(worker.ResizeEdgeAt(pointerPosition), serial))
        {
            return;
        }

        var flag = FlagOf(changed);
        buttons = pressed ? buttons | flag : buttons & ~flag;
        if (pressed)
        {
            PressSerial = serial;
        }
        else
        {
            PointerReleased?.Invoke();
        }

        if ((pressed ? worker?.InputWindow : worker?.Window) is not { } window || changed == MouseButtons.None)
        {
            return;
        }

        var args = new RawPointerEventArgs(pressed ? RawPointerEventType.Down : RawPointerEventType.Up, window,
            pointerPosition, InputModifiers(), time) { ChangedButton = changed };
        WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
    }

    private void OnAxis(uint time, WlPointer.Axis axis, double value)
    {
        wheelTime = time;
        if (axis == WlPointer.Axis.VerticalScroll)
        {
            wheelPixelsVertical += value;
        }
        else
        {
            wheelPixelsHorizontal += value;
        }
    }

    private void OnAxisValue120(WlPointer.Axis axis, int value120)
    {
        if (axis == WlPointer.Axis.VerticalScroll)
        {
            wheel120Vertical += value120;
        }
        else
        {
            wheel120Horizontal += value120;
        }
    }

    private void OnPointerFrame()
    {
        var vertical = wheel120Vertical != 0 ? wheel120Vertical : (int)Math.Round(wheelPixelsVertical / PixelsPerNotch * Notch);
        var horizontal = wheel120Horizontal != 0 ? wheel120Horizontal : (int)Math.Round(wheelPixelsHorizontal / PixelsPerNotch * Notch);
        wheel120Vertical = wheel120Horizontal = 0;
        wheelPixelsVertical = wheelPixelsHorizontal = 0;

        var window = pointerWindow?.InputWindow;
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

    private void OnPinchUpdate(uint time, double dx, double dy, double scale, double rotation)
    {
        var window = pointerWindow?.InputWindow;
        if (window == null || scale <= 0)
        {
            return;
        }

        pinchNotches += Math.Log(scale / pinchScale) / Math.Log(1.1);
        pinchScale = scale;
        var delta = (int)Math.Truncate(pinchNotches * Notch);
        if (delta == 0)
        {
            return;
        }

        pinchNotches -= (double)delta / Notch;
        var args = new RawPointerWheelEventArgs(delta, window, pointerPosition,
            InputModifiers() | Core.Input.InputModifiers.LeftControl, time, false);
        WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
    }

    private void DispatchWheel(IWindow window, int delta, bool horizontal)
    {
        var args = new RawPointerWheelEventArgs(delta, window, pointerPosition, InputModifiers(), wheelTime, horizontal);
        WaylandPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
    }

    private InputModifiers InputModifiers() => KeyModifiers() | buttons;

    // The modifier keys held, and those xkb has in effect without a key held - latched or locked by Sticky Keys.
    private Core.Input.InputModifiers KeyModifiers()
    {
        var modifiers = KeyboardDevice.CurrentDevice.Modifiers;
        var effective = xkb?.Modifiers ?? XkbModifiers.None;
        if ((effective & XkbModifiers.Shift) != 0 &&
            (modifiers & (Core.Input.InputModifiers.LeftShift | Core.Input.InputModifiers.RightShift)) == 0)
        {
            modifiers |= Core.Input.InputModifiers.LeftShift;
        }

        if ((effective & XkbModifiers.Control) != 0 &&
            (modifiers & (Core.Input.InputModifiers.LeftControl | Core.Input.InputModifiers.RightControl)) == 0)
        {
            modifiers |= Core.Input.InputModifiers.LeftControl;
        }

        if ((effective & XkbModifiers.Alt) != 0 &&
            (modifiers & (Core.Input.InputModifiers.LeftAlt | Core.Input.InputModifiers.RightAlt)) == 0)
        {
            modifiers |= Core.Input.InputModifiers.LeftAlt;
        }

        return modifiers;
    }

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
            PosixInterop.Close(fd);
            return;
        }

        xkb?.Dispose();
        xkb = XkbKeyboard.FromKeymap(fd, size);
        hasRightToLeftLayout = xkb != null && TypesRightToLeft(xkb);
    }

    private static bool TypesRightToLeft(XkbKeyboard keyboard)
    {
        var letters = new StringBuilder();
        for (uint layout = 0; layout < keyboard.LayoutCount; layout++)
        {
            letters.Clear();
            foreach (var (first, last) in EvdevLetterRows)
            {
                for (var code = first; code <= last; code++)
                {
                    var character = keyboard.Character(code, layout);
                    if (character > 0 && Rune.IsValid(character))
                    {
                        letters.Append(char.ConvertFromUtf32(character));
                    }
                }
            }

            if (BidiParagraph.DirectionOf(letters.ToString().AsSpan()) == TextDirection.RightToLeft)
            {
                return true;
            }
        }

        return false;
    }

    private void OnKeyboardEnter(uint serial, WlSurface surface, ReadOnlySpan<byte> keys)
    {
        keyboardWindow = platform.Find(surface);

        // The keys already held as the keyboard comes here: a Shift pressed in another window is down for this one too.
        if (xkb != null)
        {
            lock (_keysDownLock)
            {
                foreach (var code in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(keys))
                {
                    keysDown.Add(KeyOf(code));
                }
            }
        }

        if (keyboardWindow is { InputWindow: null, Window: not null })
        {
            keyboardWindow.ActivateBlocker();
        }
    }

    private void OnKeyboardLeave(uint serial, WlSurface surface)
    {
        keyboardWindow = null;
        StopRepeat();
        xkb?.ResetCompose();
        lock (_keysDownLock)
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
        NoteInput(serial, keyboardWindow);
        if (xkb == null)
        {
            return;
        }

        var key = KeyOf(code);
        if (state == WlKeyboard.KeyState.Pressed)
        {
            lock (_keysDownLock)
            {
                keysDown.Add(key);
            }

            Press(code, key, time, repeated: false);

            if (xkb.Repeats(code) && repeatRate > 0)
            {
                repeatCode = code;
                repeatDue = Environment.TickCount64 + repeatDelay;
            }
        }
        else
        {
            lock (_keysDownLock)
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
                var modifiers = KeyModifiers();
                WaylandPlatform.DispatchInput(() => KeyboardDevice.CurrentDevice.ProcessEvent(
                    new RawKeyboardEventArgs(key, RawKeyboardEventType.KeyUp, press, modifiers, time), window));
            }
        }
    }

    private Key KeyOf(uint code) =>
        WaylandKeys.ToKey(code, xkb.KeySym(code), (xkb.Modifiers & XkbModifiers.NumLock) != 0);

    private void Press(uint code, Key key, uint time, bool repeated)
    {
        var window = keyboardWindow?.InputWindow;
        if (window == null)
        {
            return;
        }

        var press = new KeyPressInfo
        {
            PreviousState = repeated ? KeyState.Down : KeyState.Up, CurrentState = KeyState.Down, RepeatCount = 1,
            PhysicalKey = WaylandKeys.ToHidUsage(code), PressTime = time,
        };
        var modifiers = KeyModifiers();
        WaylandPlatform.DispatchInput(() => KeyboardDevice.CurrentDevice.ProcessEvent(
            new RawKeyboardEventArgs(key, RawKeyboardEventType.KeyDown, press, modifiers, time), window));

        var held = xkb.Modifiers;
        if ((held & (XkbModifiers.Control | XkbModifiers.Alt)) != 0)
        {
            return;
        }

        var text = xkb.Text(code);
        if (!repeated)
        {
            switch (xkb.Compose(code, out var composed))
            {
                case XkbComposeStatus.Composing:
                case XkbComposeStatus.Cancelled:
                    return;
                case XkbComposeStatus.Composed:
                    text = composed;
                    break;
            }
        }

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
