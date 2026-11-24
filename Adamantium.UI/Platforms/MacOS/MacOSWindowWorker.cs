using System;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.MacOS.QuartzCore;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Internals;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>A window on macOS: an NSWindow whose content view hosts the CAMetalLayer the window is drawn into. AppKit
/// is called on the main thread; what it reports reaches the window on the loop thread.</summary>
public class MacOSWindowWorker : AdamantiumComponent, IWindowWorkerService
{
    private const int DefaultWidth = 800;
    private const int DefaultHeight = 600;
    private const int WheelNotch = 120;

    private readonly MacOSPlatform _platform;
    private IWindow _window;
    private IntPtr _nsWindow;
    private IntPtr _view;
    private IntPtr _delegate;
    private IntPtr _layer;
    private double _scale = 1;
    private NSEventModifierFlags _flags;
    private InputModifiers _buttons;
    private bool _closed;

    public MacOSWindowWorker(IUIContext context)
    {
        UIContext = context;
        _platform = (MacOSPlatform)context.Resolve<IApplicationPlatform>();
    }

    public IUIContext UIContext { get; }

    public Rect RestoreBounds
    {
        get
        {
            if (_nsWindow == IntPtr.Zero)
                return Rect.Empty;

            var frame = NSWindow.FrameOf(_nsWindow);
            var top = MainScreenHeight - (frame.Origin.Y + frame.Size.Height);
            return new Rect(frame.Origin.X * _scale, top * _scale, frame.Size.Width * _scale, frame.Size.Height * _scale);
        }
    }

    private static double MainScreenHeight => NSScreen.FrameOf(NSScreen.Main).Size.Height;

    public void SetWindow(IWindow window)
    {
        _window = window;
        window.Closed += OnWindowClosed;

        var width = InitialExtent(window.ClientWidth, window.Width, DefaultWidth);
        var height = InitialExtent(window.ClientHeight, window.Height, DefaultHeight);
        var visible = NSScreen.VisibleFrameOf(NSScreen.Main);
        var left = double.IsNaN(window.Left) ? visible.Origin.X + (visible.Size.Width - width) / 2 : window.Left;
        var top = double.IsNaN(window.Top) ? (visible.Size.Height - height) / 2 : window.Top;
        var content = new CGRect(left, MainScreenHeight - top - height, width, height);

        _nsWindow = NSWindow.Create(content, NSWindowStyleMask.Titled | NSWindowStyleMask.Closable |
                                             NSWindowStyleMask.Miniaturizable | NSWindowStyleMask.Resizable);
        NSWindow.SetTitle(_nsWindow, window.Title);

        _view = NSView.Create(MacOSContentView.Class, new CGRect(0, 0, width, height));
        ObjCManagedObject.Attach(_view, this);
        _layer = CAMetalLayer.Create();
        NSView.SetLayer(_view, _layer);
        NSWindow.SetContentView(_nsWindow, _view);
        NSWindow.MakeFirstResponder(_nsWindow, _view);

        _delegate = NSObject.New(MacOSWindowDelegate.Class);
        ObjCManagedObject.Attach(_delegate, this);
        NSWindow.SetDelegate(_nsWindow, _delegate);

        _scale = NSWindow.BackingScaleFactorOf(_nsWindow);
        SizeLayer(width, height);

        window.SetHandle(_nsWindow);
        window.SetSurfaceHandle(_layer);
        window.DpiScale = new Vector2<double>(_scale, _scale);
        window.DisplayRefreshRate = RefreshRate();
        window.UpdateClientSizeFromPlatform(width, height);

        Input.DragDrop.RegisterNativeDropTarget(window);
        UIContext.ThemeEngine.ApplyCurrentTheme(window);
        UIContext.UIApplication.AddWindow(window);
        window.OnSourceInitialized();
    }

    public void SetTitle(string title) => OnMainThread(() => NSWindow.SetTitle(_nsWindow, title));

    public void SetPosition(double left, double top) => OnMainThread(() =>
    {
        var frame = NSWindow.FrameOf(_nsWindow);
        NSWindow.SetFrame(_nsWindow, frame with
        {
            Origin = new CGPoint(left, MainScreenHeight - top - frame.Size.Height)
        });
    });

    public void SetSize(double clientWidth, double clientHeight) => OnMainThread(() =>
    {
        var frame = NSWindow.FrameOf(_nsWindow);
        var content = NSWindow.ContentRectForFrame(_nsWindow, frame);
        var top = content.Origin.Y + content.Size.Height;
        var resized = new CGRect(content.Origin.X, top - clientHeight, clientWidth, clientHeight);
        NSWindow.SetFrame(_nsWindow, NSWindow.FrameRectForContent(_nsWindow, resized));
    });

    public void UpdateOverlayTraits()
    {
    }

    public void ShowWindow(WindowState windowState) => OnMainThread(() =>
    {
        switch (windowState)
        {
            case WindowState.Minimized:
                NSWindow.OrderFront(_nsWindow);
                NSWindow.Miniaturize(_nsWindow);
                break;
            case WindowState.Maximized:
                NSWindow.MakeKeyAndOrderFront(_nsWindow);
                if (!NSWindow.IsZoomed(_nsWindow))
                    NSWindow.Zoom(_nsWindow);
                break;
            default:
                if (NSWindow.IsMiniaturized(_nsWindow))
                    NSWindow.Deminiaturize(_nsWindow);
                NSWindow.MakeKeyAndOrderFront(_nsWindow);
                break;
        }
    });

    public void HideWindow() => OnMainThread(() => NSWindow.OrderOut(_nsWindow));

    public void Activate() => OnMainThread(() =>
    {
        if (NSWindow.IsMiniaturized(_nsWindow))
            NSWindow.Deminiaturize(_nsWindow);
        NSApplication.Activate();
        NSWindow.MakeKeyAndOrderFront(_nsWindow);
    });

    public void RaiseWithoutActivation() => OnMainThread(() => NSWindow.OrderFront(_nsWindow));

    public void SetMouseCapture(bool capture)
    {
    }

    public PixelPoint ScreenToClient(PixelPoint screen)
    {
        var origin = ClientOrigin();
        return new PixelPoint(screen.X - origin.X, screen.Y - origin.Y);
    }

    public PixelPoint ClientToScreen(PixelPoint client)
    {
        var origin = ClientOrigin();
        return new PixelPoint(client.X + origin.X, client.Y + origin.Y);
    }

    public void SetRelativeMouseMode(bool enabled, PixelPoint restoreScreen)
    {
    }

    public void SetInputMethodEnabled(bool enabled)
    {
    }

    public void BeginMoveDrag()
    {
    }

    internal void OnPointer(IntPtr nsEvent, RawPointerEventType type, MouseButtons button)
    {
        var window = _window;
        if (window == null || _closed)
            return;

        if (type == RawPointerEventType.Down)
            _buttons |= ButtonModifier(button);
        else if (type == RawPointerEventType.Up)
            _buttons &= ~ButtonModifier(button);

        var args = new RawPointerEventArgs(type, window, PositionOf(nsEvent), Modifiers(nsEvent), TimeOf(nsEvent))
        {
            ChangedButton = button
        };
        MacOSPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
    }

    internal void OnScroll(IntPtr nsEvent)
    {
        var window = _window;
        if (window == null || _closed)
            return;

        var precise = NSEvent.HasPreciseScrollingDeltas(nsEvent);
        var perNotch = precise ? WheelNotch / 40.0 : WheelNotch;
        var position = PositionOf(nsEvent);
        var modifiers = Modifiers(nsEvent);
        var time = TimeOf(nsEvent);
        var vertical = (int)Math.Round(NSEvent.ScrollingDeltaYOf(nsEvent) * perNotch);
        var horizontal = (int)Math.Round(-NSEvent.ScrollingDeltaXOf(nsEvent) * perNotch);
        if (vertical != 0)
        {
            var args = new RawPointerWheelEventArgs(vertical, window, position, modifiers, time, false);
            MacOSPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
        }

        if (horizontal != 0)
        {
            var args = new RawPointerWheelEventArgs(horizontal, window, position, modifiers, time, true);
            MacOSPlatform.DispatchInput(() => PointerDevice.Current.ProcessEvent(args));
        }
    }

    internal void OnKey(IntPtr nsEvent, bool down)
    {
        var window = _window;
        if (window == null || _closed)
            return;

        var code = NSEvent.KeyCodeOf(nsEvent);
        var repeated = down && NSEvent.IsRepeat(nsEvent);
        var modifiers = Modifiers(nsEvent);
        var time = TimeOf(nsEvent);
        var press = new KeyPressInfo
        {
            PreviousState = down && !repeated ? KeyState.Up : KeyState.Down,
            CurrentState = down ? KeyState.Down : KeyState.Up,
            RepeatCount = 1,
            PhysicalKey = MacOSKeys.ToHidUsage(code),
            PressTime = time
        };
        var key = MacOSKeys.ToKey(code);
        var type = down ? RawKeyboardEventType.KeyDown : RawKeyboardEventType.KeyUp;
        MacOSPlatform.DispatchInput(() => KeyboardDevice.CurrentDevice.ProcessEvent(
            new RawKeyboardEventArgs(key, type, press, modifiers, time), window));

        var flags = NSEvent.ModifierFlagsOf(nsEvent);
        if (!down || (flags & (NSEventModifierFlags.Command | NSEventModifierFlags.Control)) != 0)
            return;

        var text = NSEvent.CharactersOf(nsEvent);
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0]) || (text[0] >= 0xF700 && text[0] <= 0xF8FF))
            return;

        MacOSPlatform.DispatchInput(() => KeyboardDevice.CurrentDevice.ProcessEvent(
            new RawTextInputEventArgs(text, modifiers, time), window));
    }

    internal void OnFlagsChanged(IntPtr nsEvent)
    {
        var flags = NSEvent.ModifierFlagsOf(nsEvent);
        var changed = flags ^ _flags;
        _flags = flags;
        var mask = NSEventModifierFlags.Shift | NSEventModifierFlags.Control | NSEventModifierFlags.Option |
                   NSEventModifierFlags.Command | NSEventModifierFlags.CapsLock;
        if ((changed & mask) == 0)
            return;

        OnKey(nsEvent, (flags & changed & mask) != 0);
    }

    internal void OnCloseRequested()
    {
        MacOSPlatform.DispatchInput(() => _window?.Close());
    }

    internal void OnResized()
    {
        var bounds = NSView.BoundsOf(_view);
        SizeLayer(bounds.Size.Width, bounds.Size.Height);
        var state = NSWindow.IsZoomed(_nsWindow) ? WindowState.Maximized : WindowState.Normal;
        MacOSPlatform.DispatchInput(() =>
        {
            if (_closed)
                return;

            _window.State = state;
            _window.UpdateClientSizeFromPlatform(bounds.Size.Width, bounds.Size.Height);
        });
    }

    internal void OnMoved()
    {
        var content = NSWindow.ContentRectForFrame(_nsWindow, NSWindow.FrameOf(_nsWindow));
        var left = content.Origin.X;
        var top = MainScreenHeight - content.Origin.Y - content.Size.Height;
        MacOSPlatform.DispatchInput(() =>
        {
            if (!_closed)
                _window.UpdatePositionFromPlatform(left, top);
        });
    }

    internal void OnScaleChanged()
    {
        _scale = NSWindow.BackingScaleFactorOf(_nsWindow);
        var bounds = NSView.BoundsOf(_view);
        SizeLayer(bounds.Size.Width, bounds.Size.Height);
        var scale = new Vector2<double>(_scale, _scale);
        var rate = RefreshRate();
        MacOSPlatform.DispatchInput(() =>
        {
            if (_closed)
                return;

            _window.DpiScale = scale;
            _window.DisplayRefreshRate = rate;
        });
    }

    internal void OnKeyWindowChanged(bool isKey)
    {
        MacOSPlatform.DispatchInput(() =>
        {
            if (_closed)
                return;

            if (isKey)
                _window.BecomeActive(UIContext.UIApplication);
            else
                _window.StopBeingActive(UIContext.UIApplication);
        });
    }

    internal void OnMiniaturized(bool miniaturized)
    {
        ((UIApplication)UIContext.UIApplication).SetWindowHidden(_window, miniaturized);
        MacOSPlatform.DispatchInput(() =>
        {
            if (!_closed)
                _window.State = miniaturized ? WindowState.Minimized : WindowState.Normal;
        });
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        if (_closed)
            return;

        _closed = true;
        Input.DragDrop.UnregisterNativeDropTarget(_window);
        UIContext.UIApplication.RemoveWindow(_window);
        _window.SetHandle(IntPtr.Zero);
        OnMainThread(() =>
        {
            NSWindow.SetDelegate(_nsWindow, IntPtr.Zero);
            NSWindow.Close(_nsWindow);
            ObjCManagedObject.Release(_view);
            ObjCManagedObject.Release(_delegate);
            NSObject.Release(_delegate);
            NSObject.Release(_view);
            NSObject.Release(_nsWindow);
        });
    }

    private void SizeLayer(double width, double height)
    {
        CAMetalLayer.SetContentsScale(_layer, _scale);
        CAMetalLayer.SetDrawableSize(_layer, new CGSize(width * _scale, height * _scale));
    }

    private double RefreshRate()
    {
        var screen = NSWindow.ScreenOf(_nsWindow);
        return NSScreen.MaximumFramesPerSecondOf(screen != IntPtr.Zero ? screen : NSScreen.Main);
    }

    private Vector2<double> PositionOf(IntPtr nsEvent)
    {
        var location = NSEvent.LocationInWindowOf(nsEvent);
        var height = NSView.BoundsOf(_view).Size.Height;
        return new Vector2<double>(location.X, height - location.Y);
    }

    private PixelPoint ClientOrigin()
    {
        var content = NSWindow.ContentRectForFrame(_nsWindow, NSWindow.FrameOf(_nsWindow));
        var top = MainScreenHeight - content.Origin.Y - content.Size.Height;
        return new PixelPoint((int)Math.Round(content.Origin.X * _scale), (int)Math.Round(top * _scale));
    }

    private static InputModifiers ButtonModifier(MouseButtons button) => button switch
    {
        MouseButtons.Left => InputModifiers.LeftMouseButton,
        MouseButtons.Right => InputModifiers.RightMouseButton,
        MouseButtons.Middle => InputModifiers.MiddleMouseButton,
        _ => InputModifiers.None
    };

    private InputModifiers Modifiers(IntPtr nsEvent)
    {
        var flags = NSEvent.ModifierFlagsOf(nsEvent);
        var modifiers = _buttons;
        if ((flags & NSEventModifierFlags.Shift) != 0)
            modifiers |= InputModifiers.LeftShift;
        if ((flags & NSEventModifierFlags.Command) != 0)
            modifiers |= InputModifiers.LeftControl;
        if ((flags & NSEventModifierFlags.Control) != 0)
            modifiers |= InputModifiers.LeftWindows;
        if ((flags & NSEventModifierFlags.Option) != 0)
            modifiers |= InputModifiers.LeftAlt;

        return modifiers;
    }

    private static uint TimeOf(IntPtr nsEvent) => (uint)(NSEvent.TimestampOf(nsEvent) * 1000);

    private static double InitialExtent(double preferred, double fallback, int defaultExtent)
    {
        if (!double.IsNaN(preferred) && preferred > 0)
            return preferred;

        return !double.IsNaN(fallback) && fallback > 0 ? fallback : defaultExtent;
    }

    private void OnMainThread(Action action)
    {
        if (_nsWindow != IntPtr.Zero)
            _platform.RunOnMainThread(action);
    }
}
