using System;
using System.Collections.Generic;
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
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>A window on macOS: an NSWindow whose content view hosts the CAMetalLayer the window is drawn into. AppKit
/// is called on the main thread; what it reports reaches the window on the loop thread.</summary>
public class MacOSWindowWorker : AdamantiumComponent, IWindowWorkerService
{
    private const int DefaultWidth = 800;
    private const int DefaultHeight = 600;
    private const int WheelNotch = 120;
    private const int MinWidth = 320;
    private const int MinHeight = 120;

    private readonly MacOSPlatform _platform;
    private IWindow _window;
    private IntPtr _nsWindow;
    private IntPtr _view;
    private IntPtr _delegate;
    private IntPtr _layer;
    private IntPtr _owner;
    private double _scale = 1;
    private volatile Placement _placement = new(Rect.Empty, default);
    private InputModifiers _buttons;
    private IntPtr _pressEvent;
    private CGRect _restoreFrame;
    private bool _topmost;
    private volatile bool _closed;
    private readonly List<MacOSWindowWorker> _blockedWindows = [];
    private readonly HashSet<MacOSWindowWorker> _blockers = [];
    private readonly HashSet<MacOSWindowWorker> _exempt = [];
    private bool _blocking;
    private volatile bool _inputBlocked;

    public MacOSWindowWorker(IUIContext context)
    {
        UIContext = context;
        _platform = (MacOSPlatform)context.Resolve<IApplicationPlatform>();
    }

    public IUIContext UIContext { get; }

    public Rect RestoreBounds => _placement.RestoreBounds;

    // Read on the main thread, asked for on any.
    private void UpdatePlacement()
    {
        var frame = NSWindow.FrameOf(_nsWindow);
        var client = TopLeftOf(NSWindow.ContentRectForFrame(_nsWindow, frame));
        var restore = IsMaximized && _restoreFrame.Size.Width > 0 ? _restoreFrame : frame;
        var corner = TopLeftOf(restore);
        _placement = new Placement(
            new Rect(corner.X, corner.Y, restore.Size.Width * _scale, restore.Size.Height * _scale),
            new PixelPoint((int)Math.Round(client.X), (int)Math.Round(client.Y)));
    }

    // In physical pixels of the desktop.
    private Vector2<double> TopLeftOf(CGRect frame) =>
        _platform.Desktop.ToPixels(frame.Origin.X, frame.Origin.Y + frame.Size.Height);

    private bool IsFullScreen => (NSWindow.StyleMaskOf(_nsWindow) & NSWindowStyleMask.FullScreen) != 0;

    // AppKit calls every window that cannot be resized zoomed.
    private bool IsZoomed => (NSWindow.StyleMaskOf(_nsWindow) & NSWindowStyleMask.Resizable) != 0 &&
                             NSWindow.IsZoomed(_nsWindow);

    private bool IsMaximized => IsFullScreen || IsZoomed;

    public void SetWindow(IWindow window)
    {
        // AppKit aborts the process over a window made on any other thread.
        if (!_platform.IsOnUIThread)
            throw new Exceptions.DispatcherException("A window is made on the main thread: use IUIApplication.ExecuteOnUIThread");

        _window = window;
        window.Closed += OnWindowClosed;

        var width = InitialExtent(window.ClientWidth, window.Width, DefaultWidth);
        var height = InitialExtent(window.ClientHeight, window.Height, DefaultHeight);
        var corner = _platform.Desktop.ToPoints(double.IsNaN(window.Left) ? 0 : window.Left,
            double.IsNaN(window.Top) ? 0 : window.Top);
        var content = new CGRect(corner.X, corner.Y - height, width, height);

        var style = StyleFor(window);
        _nsWindow = NSWindow.Create(content, style);
        NSWindow.SetAllowsFullScreen(_nsWindow, (style & NSWindowStyleMask.Resizable) != 0);
        MoveTo(corner);
        NSWindow.SetTitle(_nsWindow, window.Title);
        if (window.UseCustomChrome && window.ActivateOnShow)
            NSWindow.HideTitleBar(_nsWindow);
        NSWindow.SetContentMinSize(_nsWindow, new CGSize(MinExtent(window.MinWidth, MinWidth),
            MinExtent(window.MinHeight, MinHeight)));
        NSWindow.SetContentMaxSize(_nsWindow, new CGSize(MaxExtent(window.MaxWidth), MaxExtent(window.MaxHeight)));

        _view = NSView.Create(MacOSContentView.Class, new CGRect(0, 0, width, height));
        ObjCManagedObject.Attach(_view, this);
        NSView.TrackMouse(_view);
        _layer = CAMetalLayer.Create();
        ApplyOverlayTraits(OverlayTraits.Of(window));
        NSView.SetLayer(_view, _layer);
        NSWindow.SetContentView(_nsWindow, _view);
        NSWindow.MakeFirstResponder(_nsWindow, _view);

        _delegate = NSObject.New(MacOSWindowDelegate.Class);
        ObjCManagedObject.Attach(_delegate, this);
        NSWindow.SetDelegate(_nsWindow, _delegate);

        _scale = NSWindow.BackingScaleFactorOf(_nsWindow);
        SizeLayer(width, height);
        UpdatePlacement();

        window.SetHandle(_nsWindow);
        window.SetSurfaceHandle(_layer);
        window.DpiScale = new Vector2<double>(_scale, _scale);
        window.DisplayRefreshRate = RefreshRate();
        var frame = NSWindow.FrameOf(_nsWindow);
        window.Width = frame.Size.Width * _scale;
        window.Height = frame.Size.Height * _scale;
        window.UpdateClientSizeFromPlatform(width, height);

        if (window.Owner != null)
            ParentTo(window.Owner.Handle);

        Input.DragDrop.RegisterNativeDropTarget(window);
        UIContext.ThemeEngine.ApplyCurrentTheme(window);
        UIContext.UIApplication.AddWindow(window);
        _platform.Register(this);
        window.OnSourceInitialized();
        window.StateChanged += WindowOnStateChanged;
        window.ResizeModeChanged += WindowOnResizeModeChanged;
    }

    public bool AppliesWindowOpacity => true;

    public void SetOwner(IWindow owner)
    {
        var handle = owner?.Handle ?? IntPtr.Zero;
        OnMainThread(() => ParentTo(handle));
    }

    public void UpdateModal()
    {
        lock (_platform.ModalLock)
        {
            var modal = _window.IsModal && !_closed;
            if (modal == _blocking)
                return;

            _blocking = modal;
            if (modal)
            {
                _exempt.UnionWith(_blockers);
                _platform.CollectOthers(this, _blockedWindows);
                foreach (var other in _blockedWindows)
                    other._blockers.Add(this);
            }
            else
            {
                foreach (var other in _blockedWindows)
                    other._blockers.Remove(this);
            }

            Recount();
            foreach (var other in _blockedWindows)
                other.Recount();

            if (!modal)
            {
                _blockedWindows.Clear();
                _exempt.Clear();
            }
        }
    }

    private void Recount()
    {
        var blocked = false;
        foreach (var blocker in _blockers)
        {
            if (!_blocking || !_exempt.Contains(blocker))
            {
                blocked = true;
                break;
            }
        }

        if (blocked == _inputBlocked)
            return;

        _inputBlocked = blocked;
        if (blocked)
            MacOSPlatform.DispatchInput(LetGo);
    }

    private void LetGo()
    {
        if (!_closed)
            BlockedWindowInput.LetGo(_window);
    }

    // Of modals opened over modals, the one nothing keeps from input itself.
    private void ActivateBlocker()
    {
        MacOSWindowWorker modal = null;
        lock (_platform.ModalLock)
        {
            foreach (var blocker in _blockers)
            {
                if (blocker._blocking && (modal == null || !blocker._inputBlocked))
                    modal = blocker;
            }
        }

        modal?.Activate();
    }

    public void SetTitle(string title) => OnMainThread(() => NSWindow.SetTitle(_nsWindow, title));

    public void SetPosition(double left, double top) => OnMainThread(() => MoveTo(_platform.Desktop.ToPoints(left, top)));

    private void MoveTo(CGPoint topLeft)
    {
        var frame = NSWindow.FrameOf(_nsWindow);
        NSWindow.SetFrame(_nsWindow, frame with { Origin = new CGPoint(topLeft.X, topLeft.Y - frame.Size.Height) });
    }

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
        var traits = OverlayTraits.Of(_window);
        OnMainThread(() => ApplyOverlayTraits(traits));
    }

    public void ShowWindow(WindowState windowState)
    {
        var activate = _window.ActivateOnShow;
        OnMainThread(() =>
        {
            switch (windowState)
            {
                case WindowState.Minimized:
                    Minimize();
                    return;
                case WindowState.Maximized:
                    if (NSWindow.IsMiniaturized(_nsWindow))
                        NSWindow.Deminiaturize(_nsWindow);
                    Present(activate);
                    if (!IsMaximized)
                        NSWindow.Zoom(_nsWindow);
                    return;
                default:
                    // From the Dock the window comes back as it went, zoomed or not, as a click on its Dock tile brings it.
                    if (NSWindow.IsMiniaturized(_nsWindow))
                        NSWindow.Deminiaturize(_nsWindow);
                    else if (IsFullScreen)
                        NSWindow.ToggleFullScreen(_nsWindow);
                    else if (IsZoomed)
                        NSWindow.Zoom(_nsWindow);
                    Present(activate);
                    return;
            }
        });
    }

    private void Minimize()
    {
        // A window on a full screen of its own does not go to the Dock.
        if (IsFullScreen)
        {
            MacOSPlatform.DispatchInput(() => SetStateFromPlatform(WindowState.Maximized));
            return;
        }

        LeaveOwner();
        NSWindow.OrderFront(_nsWindow);
        NSWindow.Miniaturize(_nsWindow);
        // At once, not when the delegate is told the window has arrived in the Dock.
        MacOSPlatform.DispatchInput(StopBeingActive);
    }

    private void Present(bool activate)
    {
        if (activate)
            NSWindow.MakeKeyAndOrderFront(_nsWindow);
        else
            NSWindow.OrderFront(_nsWindow);
        JoinOwner();
        _platform.JoinOwned(_nsWindow);
    }

    internal void JoinIfOwnedBy(IntPtr owner)
    {
        if (_owner == owner && _nsWindow != IntPtr.Zero && NSWindow.IsVisible(_nsWindow) &&
            NSWindow.ParentOf(_nsWindow) != owner)
        {
            JoinOwner();
        }
    }

    internal void LeaveIfOwnedBy(IntPtr owner)
    {
        if (_owner == owner && _nsWindow != IntPtr.Zero)
            LeaveOwner();
    }

    public void HideWindow() => OnMainThread(() =>
    {
        LeaveOwner();
        _platform.LeaveOwned(_nsWindow);
        NSWindow.OrderOut(_nsWindow);
    });

    public void Activate() => OnMainThread(() =>
    {
        if (NSWindow.IsMiniaturized(_nsWindow))
            NSWindow.Deminiaturize(_nsWindow);
        NSApplication.Activate();
        NSWindow.MakeKeyAndOrderFront(_nsWindow);
        _platform.JoinOwned(_nsWindow);
    });

    public void RaiseWithoutActivation() => OnMainThread(() => NSWindow.OrderFront(_nsWindow));

    public void SetMouseCapture(bool capture)
    {
    }

    public PixelPoint ScreenToClient(PixelPoint screen)
    {
        var origin = _placement.ClientOrigin;
        return new PixelPoint(screen.X - origin.X, screen.Y - origin.Y);
    }

    public PixelPoint ClientToScreen(PixelPoint client)
    {
        var origin = _placement.ClientOrigin;
        return new PixelPoint(client.X + origin.X, client.Y + origin.Y);
    }

    public void SetRelativeMouseMode(bool enabled, PixelPoint restoreScreen)
    {
    }

    public void SetInputMethodEnabled(bool enabled)
    {
    }

    public void BeginMoveDrag() => OnMainThread(() =>
    {
        if (_pressEvent == IntPtr.Zero)
            return;

        // The drag keeps the mouse's release to itself, so the window is told of it here.
        NSWindow.PerformWindowDrag(_nsWindow, _pressEvent);
        OnPointer(_pressEvent, RawPointerEventType.Up, MouseButtons.Left);
    });

    internal void OnPointer(IntPtr nsEvent, RawPointerEventType type, MouseButtons button)
    {
        var window = _window;
        if (window == null || _closed)
            return;

        var held = ButtonModifier(button);
        if (type == RawPointerEventType.Down)
            _buttons |= held;
        else if (type == RawPointerEventType.Up)
        {
            if (held != InputModifiers.None && (_buttons & held) == 0)
                return;   // told of already, when the window's drag began

            _buttons &= ~held;
        }

        // A release still goes to the window: what was pressed before the modal one opened is let go of.
        if (_inputBlocked && type != RawPointerEventType.Up && type != RawPointerEventType.LeaveWindow)
        {
            if (type == RawPointerEventType.Down)
                ActivateBlocker();
            return;
        }

        if (type == RawPointerEventType.Down && button == MouseButtons.Left)
        {
            NSObject.Release(_pressEvent);
            _pressEvent = NSObject.Retain(nsEvent);
        }

        var args = new RawPointerEventArgs(type, window, PositionOf(nsEvent), Modifiers(nsEvent), TimeOf(nsEvent))
        {
            ChangedButton = button
        };
        if (type == RawPointerEventType.Up && button == MouseButtons.Left)
        {
            NSObject.Release(_pressEvent);
            _pressEvent = IntPtr.Zero;
        }

        Report(() => PointerDevice.Current.ProcessEvent(args));
    }

    // What was on its way when the window closed goes nowhere.
    private void Report(Action raise) => MacOSPlatform.DispatchInput(() =>
    {
        if (!_closed)
            raise();
    });

    internal void OnScroll(IntPtr nsEvent)
    {
        var window = _window;
        if (window == null || _closed || _inputBlocked)
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
            Report(() => PointerDevice.Current.ProcessEvent(args));
        }

        if (horizontal != 0)
        {
            var args = new RawPointerWheelEventArgs(horizontal, window, position, modifiers, time, true);
            Report(() => PointerDevice.Current.ProcessEvent(args));
        }
    }

    internal void OnKey(IntPtr nsEvent, bool down)
    {
        var window = _window;
        if (window == null || _closed || _inputBlocked)
            return;

        var flags = NSEvent.ModifierFlagsOf(nsEvent);
        if (IsFullScreenShortcut(nsEvent, flags))
        {
            if (down && !NSEvent.IsRepeat(nsEvent) &&
                (NSWindow.StyleMaskOf(_nsWindow) & NSWindowStyleMask.Resizable) != 0)
            {
                NSWindow.ToggleFullScreen(_nsWindow);
            }

            return;
        }

        ReportKey(window, nsEvent, down, down && NSEvent.IsRepeat(nsEvent));

        if (!down || (flags & (NSEventModifierFlags.Command | NSEventModifierFlags.Control)) != 0)
            return;

        var text = NSEvent.CharactersOf(nsEvent);
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0]) || (text[0] >= 0xF700 && text[0] <= 0xF8FF))
            return;

        var modifiers = Modifiers(nsEvent);
        var time = TimeOf(nsEvent);
        Report(() => KeyboardDevice.CurrentDevice.ProcessEvent(
            new RawTextInputEventArgs(text, modifiers, time), window));
    }

    // ⌃⌘F, as every Mac application has it.
    private static bool IsFullScreenShortcut(IntPtr nsEvent, NSEventModifierFlags flags)
    {
        const NSEventModifierFlags shortcut = NSEventModifierFlags.Command | NSEventModifierFlags.Control;
        const NSEventModifierFlags held = shortcut | NSEventModifierFlags.Shift | NSEventModifierFlags.Option;
        return (flags & held) == shortcut && MacOSKeys.ToKey(NSEvent.KeyCodeOf(nsEvent)) == Key.F;
    }

    internal void OnFlagsChanged(IntPtr nsEvent)
    {
        var window = _window;
        if (window == null || _closed)
            return;

        var held = HeldBy(MacOSKeys.ToKey(NSEvent.KeyCodeOf(nsEvent)));
        var down = (NSEvent.ModifierFlagsOf(nsEvent) & held) != 0;
        if (held != 0 && !(down && _inputBlocked))
            ReportKey(window, nsEvent, down, false);
    }

    // The flag of the one key, left or right: the event says which key changed, the flag whether it is held now.
    private static NSEventModifierFlags HeldBy(Key key) => key switch
    {
        Key.LeftShift => NSEventModifierFlags.LeftShift,
        Key.RightShift => NSEventModifierFlags.RightShift,
        Key.LeftCtrl => NSEventModifierFlags.LeftCommand,
        Key.RightCtrl => NSEventModifierFlags.RightCommand,
        Key.LeftWin => NSEventModifierFlags.LeftControl,
        Key.RightWin => NSEventModifierFlags.RightControl,
        Key.LeftAlt => NSEventModifierFlags.LeftOption,
        Key.RightAlt => NSEventModifierFlags.RightOption,
        Key.CapsLock => NSEventModifierFlags.CapsLock,
        _ => 0
    };

    private void ReportKey(IWindow window, IntPtr nsEvent, bool down, bool repeated)
    {
        var code = NSEvent.KeyCodeOf(nsEvent);
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
        Report(() => KeyboardDevice.CurrentDevice.ProcessEvent(
            new RawKeyboardEventArgs(key, type, press, modifiers, time), window));
    }

    internal void OnCloseRequested()
    {
        if (!_inputBlocked)
            MacOSPlatform.DispatchInput(() => _window?.Close());
    }

    /// <summary>The window is about to be zoomed or to take a full screen: where it is now is where it comes back to.
    /// </summary>
    internal void OnLeavingNormal()
    {
        if (!IsMaximized)
            _restoreFrame = NSWindow.FrameOf(_nsWindow);
    }

    internal void OnResized()
    {
        var bounds = NSView.BoundsOf(_view);
        SizeLayer(bounds.Size.Width, bounds.Size.Height);
        UpdatePlacement();
        var state = IsMaximized ? WindowState.Maximized : WindowState.Normal;
        var frame = NSWindow.FrameOf(_nsWindow);
        var width = frame.Size.Width * _scale;
        var height = frame.Size.Height * _scale;
        MacOSPlatform.DispatchInput(() =>
        {
            if (_closed)
                return;

            _window.Width = width;
            _window.Height = height;
            if (_window.State != WindowState.Minimized)
                SetStateFromPlatform(state);
            _window.UpdateClientSizeFromPlatform(bounds.Size.Width, bounds.Size.Height);
        });
    }

    internal void OnMoved()
    {
        UpdatePlacement();
        var corner = TopLeftOf(NSWindow.FrameOf(_nsWindow));
        MacOSPlatform.DispatchInput(() =>
        {
            if (!_closed)
                _window.UpdatePositionFromPlatform(corner.X, corner.Y);
        });
    }

    internal void OnScaleChanged()
    {
        _scale = NSWindow.BackingScaleFactorOf(_nsWindow);
        var bounds = NSView.BoundsOf(_view);
        SizeLayer(bounds.Size.Width, bounds.Size.Height);
        UpdatePlacement();
        var scale = new Vector2<double>(_scale, _scale);
        var rate = RefreshRate();
        var frame = NSWindow.FrameOf(_nsWindow);
        var width = frame.Size.Width * _scale;
        var height = frame.Size.Height * _scale;
        MacOSPlatform.DispatchInput(() =>
        {
            if (_closed)
                return;

            _window.DpiScale = scale;
            _window.Width = width;
            _window.Height = height;
            _window.DisplayRefreshRate = rate;
        });
    }

    internal void OnKeyWindowChanged(bool isKey)
    {
        // A window a modal one keeps from input is not the active one: the keyboard coming to it is handed on.
        if (isKey && _inputBlocked)
        {
            ActivateBlocker();
            return;
        }

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
        if (!miniaturized)
        {
            JoinOwner();
            _platform.JoinOwned(_nsWindow);
        }

        var restored = IsMaximized ? WindowState.Maximized : WindowState.Normal;
        MacOSPlatform.DispatchInput(() =>
        {
            if (_closed)
                return;

            SetStateFromPlatform(miniaturized ? WindowState.Minimized : restored);
            if (miniaturized)
                StopBeingActive();
        });
    }

    // The application's only window goes to the Dock without its delegate hearing it resign.
    private void StopBeingActive()
    {
        if (!_closed && _window.IsActive)
            _window.StopBeingActive(UIContext.UIApplication);
    }

    // What AppKit did reaches the window without being sent back to AppKit as a request.
    private void SetStateFromPlatform(WindowState state)
    {
        if (_window.State == state)
            return;

        _window.StateChanged -= WindowOnStateChanged;
        _window.State = state;
        _window.StateChanged += WindowOnStateChanged;
    }

    private void WindowOnStateChanged(object sender, StateChangedEventArgs e) => ShowWindow(e.State);

    private void WindowOnResizeModeChanged(object sender, EventArgs e)
    {
        var style = StyleFor(_window);
        var hidesTitleBar = _window.UseCustomChrome && _window.ActivateOnShow;
        OnMainThread(() =>
        {
            // The full screen bit is AppKit's: it throws when a style set from outside changes it.
            NSWindow.SetStyleMask(_nsWindow, style | (NSWindow.StyleMaskOf(_nsWindow) & NSWindowStyleMask.FullScreen));
            NSWindow.SetAllowsFullScreen(_nsWindow, (style & NSWindowStyleMask.Resizable) != 0);
            if (hidesTitleBar)
                NSWindow.HideTitleBar(_nsWindow);
        });
    }

    // A window that takes no activation - a menu, a drag's compass - is bare: AppKit gives such a window no keyboard.
    private static NSWindowStyleMask StyleFor(IWindow window)
    {
        if (window.UseCustomChrome && !window.ActivateOnShow)
            return NSWindowStyleMask.Borderless;

        var style = NSWindowStyleMask.Titled | NSWindowStyleMask.Closable;
        if (window.ResizeMode != WindowResizeMode.NoResize)
            style |= NSWindowStyleMask.Miniaturizable;
        if (window.ResizeMode is WindowResizeMode.CanResize or WindowResizeMode.CanResizeWithGrip)
            style |= NSWindowStyleMask.Resizable;
        if (window.UseCustomChrome)
            style |= NSWindowStyleMask.FullSizeContentView;

        return style;
    }

    private void ApplyOverlayTraits(OverlayTraits traits)
    {
        _topmost = traits.Topmost;
        ApplyLevel();
        NSWindow.SetAlphaValue(_nsWindow, double.IsNaN(traits.Opacity) ? 1 : Math.Clamp(traits.Opacity, 0, 1));
        NSWindow.SetIgnoresMouseEvents(_nsWindow, traits.TransparentToInput);
        CAMetalLayer.SetOpaque(_layer, !traits.SeeThrough);
        if (traits.SeeThrough)
            NSWindow.MakeTransparent(_nsWindow);
        else
            NSWindow.MakeOpaque(_nsWindow);
        NSWindow.SetHasShadow(_nsWindow, traits.HasShadow);
        if (NSWindow.IsVisible(_nsWindow))
        {
            LeaveOwner();
            JoinOwner();
        }
    }

    private void ParentTo(IntPtr owner)
    {
        if (_owner == owner)
            return;

        LeaveOwner();
        if (owner != IntPtr.Zero)
            NSObject.Retain(owner);
        NSObject.Release(_owner);
        _owner = owner;
        if (NSWindow.IsVisible(_nsWindow))
            JoinOwner();
    }

    internal void JoinOwned()
    {
        if (_nsWindow != IntPtr.Zero && NSWindow.IsVisible(_nsWindow))
            _platform.JoinOwned(_nsWindow);
    }

    // Only once both are shown: a child window is put on screen, or off it, with its owner.
    private void JoinOwner()
    {
        if (_owner == IntPtr.Zero || !NSWindow.IsVisible(_owner))
            return;

        // It takes its owner's level, and floats over an ordinary owner only when it is itself on top.
        NSWindow.AddChildWindow(_owner, _nsWindow);
        if (_topmost)
            ApplyLevel();
    }

    private void LeaveOwner()
    {
        if (_owner == IntPtr.Zero)
            return;

        NSWindow.RemoveChildWindow(_owner, _nsWindow);
        ApplyLevel();
    }

    private void ApplyLevel() => NSWindow.SetLevel(_nsWindow, _topmost ? NSWindow.FloatingLevel : NSWindow.NormalLevel);

    private static double MinExtent(double extent, double fallback) =>
        !double.IsNaN(extent) && extent > 0 ? extent : fallback;

    private static double MaxExtent(double extent) =>
        !double.IsNaN(extent) && !double.IsInfinity(extent) && extent > 0 ? extent : float.MaxValue;

    private void OnWindowClosed(object sender, EventArgs e)
    {
        if (_closed)
            return;

        _closed = true;
        _window.StateChanged -= WindowOnStateChanged;
        _window.ResizeModeChanged -= WindowOnResizeModeChanged;
        _platform.Unregister(this);
        lock (_platform.ModalLock)
        {
            foreach (var blocker in _blockers)
                blocker._blockedWindows.Remove(this);
            _blockers.Clear();
        }

        Input.DragDrop.UnregisterNativeDropTarget(_window);
        UIContext.UIApplication.RemoveWindow(_window);
        _window.SetHandle(IntPtr.Zero);
        OnMainThread(() =>
        {
            NSWindow.SetDelegate(_nsWindow, IntPtr.Zero);
            ParentTo(IntPtr.Zero);
            _platform.LeaveOwned(_nsWindow);
            NSWindow.Close(_nsWindow);
            ObjCManagedObject.Release(_view);
            ObjCManagedObject.Release(_delegate);
            NSObject.Release(_delegate);
            NSObject.Release(_view);
            NSObject.Release(_pressEvent);
            NSObject.Release(_nsWindow);
            _pressEvent = IntPtr.Zero;
            _view = IntPtr.Zero;
            _delegate = IntPtr.Zero;
            _nsWindow = IntPtr.Zero;
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

    // The window may be gone by the time the main thread comes to it: it is let go of there.
    private void OnMainThread(Action action)
    {
        if (_nsWindow == IntPtr.Zero)
            return;

        _platform.RunOnMainThread(() =>
        {
            if (_nsWindow != IntPtr.Zero)
                action();
        });
    }

    private sealed record Placement(Rect RestoreBounds, PixelPoint ClientOrigin);

    private readonly record struct OverlayTraits(bool Topmost, double Opacity, bool TransparentToInput, bool SeeThrough,
        bool HasShadow)
    {
        internal static OverlayTraits Of(IWindow window) => new(window.Topmost, window.WindowOpacity,
            window.TransparentToInput, window.UseTransparentComposition,
            window.ShowWindowBorder && !window.UseTransparentComposition);
    }
}
