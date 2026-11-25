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
public class MacOSWindowWorker : AdamantiumComponent, IWindowWorkerService, ITextInputTarget
{
    private const int DefaultWidth = 800;
    private const int DefaultHeight = 600;
    private const int WheelNotch = 120;
    private const int MinWidth = 320;
    private const int MinHeight = 120;
    private const double SmallestPinch = 0.01;

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
    private bool _relativeMouse;
    private bool _wantsRelativeMouse;
    private bool _fingersRest;
    private volatile nint _number;
    private double _pinchNotches;
    private CGRect _restoreFrame;
    private bool _topmost;
    private volatile bool _closed;
    private volatile bool _hidesComposition;
    private MacOSModality _modality;

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
        _modality = new MacOSModality(_platform, window, Activate);
        TextInput = new MacOSTextInput(this);
        FocusManager.FocusMoved += OnFocusMoved;
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
        _platform.Register(_modality);
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

    public void UpdateModal() => _modality.Update();

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

    /// <summary>Whether the window server's number of the window is this; its handle when so.</summary>
    internal bool IsNumbered(nint number, out IntPtr handle)
    {
        handle = _nsWindow;
        return number > 0 && number == _number && handle != IntPtr.Zero;
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
        // It has one once it is on screen.
        _number = NSWindow.NumberOf(_nsWindow);
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
        LeaveRelativeMouse();
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

    // The cursor stays where it was let go of, so it comes back there by itself.
    public void SetRelativeMouseMode(bool enabled, PixelPoint restoreScreen) => OnMainThread(() =>
    {
        _wantsRelativeMouse = enabled;
        if (!enabled)
            LeaveRelativeMouse();
        else
            EnterRelativeMouse();
    });

    // The window takes the keyboard for it: a right press gives none, and it is by losing it that the cursor is let go of.
    // In an application that is not active it waits for that.
    private void EnterRelativeMouse()
    {
        if (_relativeMouse || !_wantsRelativeMouse || !NSApplication.IsActive)
            return;

        if (!NSWindow.IsKeyWindow(_nsWindow))
            NSWindow.MakeKeyAndOrderFront(_nsWindow);
        if (_relativeMouse || !NSWindow.IsKeyWindow(_nsWindow))
            return;

        BringCursorIn();
        _relativeMouse = true;
        _platform.IsMouseRelative = true;
        CoreGraphicsInterop.AssociateMouseAndCursor(false);
        NSCursor.Hide();
    }

    // The mouse's moves come to what the cursor is over, and it may be elsewhere when looking begins from the keyboard.
    private void BringCursorIn()
    {
        var client = NSWindow.ContentRectForFrame(_nsWindow, NSWindow.FrameOf(_nsWindow));
        var cursor = NSEvent.MouseLocation;
        var screens = NSScreen.All;
        if (screens.Length == 0 ||
            cursor.X >= client.Origin.X && cursor.X < client.Origin.X + client.Size.Width &&
            cursor.Y >= client.Origin.Y && cursor.Y < client.Origin.Y + client.Size.Height)
        {
            return;
        }

        var fromTop = NSScreen.FrameOf(screens[0]).Size.Height - (client.Origin.Y + client.Size.Height / 2);
        CoreGraphicsInterop.WarpCursor(new CGPoint(client.Origin.X + client.Size.Width / 2, fromTop));
    }

    private void LeaveRelativeMouse()
    {
        if (!_relativeMouse)
            return;

        _relativeMouse = false;
        _platform.IsMouseRelative = false;
        CoreGraphicsInterop.AssociateMouseAndCursor(true);
        NSCursor.Unhide();
    }

    public void SetInputMethodEnabled(bool enabled)
    {
        _hidesComposition = !enabled;
        OnMainThread(() =>
        {
            LeaveComposition(TextInput.Discard);
            // What types letters by composing them, as Chinese, is left out; dead keys are not.
            NSTextInputContext.AllowOnlyRoman(NSTextInputContext.Of(_view), !enabled);
        });
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
        if (_modality.IsBlocked && type != RawPointerEventType.Up && type != RawPointerEventType.LeaveWindow)
        {
            if (type == RawPointerEventType.Down)
                _modality.ActivateBlocker();
            return;
        }

        if (_relativeMouse && type is RawPointerEventType.Move or RawPointerEventType.LeaveWindow)
        {
            if (type == RawPointerEventType.Move)
                ReportDelta(window, nsEvent);
            return;
        }

        if (type == RawPointerEventType.Down)
            LeaveComposition();

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
        if (window == null || _closed)
            return;

        var precise = NSEvent.HasPreciseScrollingDeltas(nsEvent);
        var perNotch = precise ? WheelNotch / 40.0 : WheelNotch;
        var position = PositionOf(nsEvent);
        var modifiers = Modifiers(nsEvent);
        var time = TimeOf(nsEvent);
        var across = NSEvent.ScrollingDeltaXOf(nsEvent);
        var down = NSEvent.ScrollingDeltaYOf(nsEvent);
        var vertical = (int)Math.Round(down * perNotch);
        var horizontal = (int)Math.Round(-across * perNotch);
        var pan = precise ? PanOf(nsEvent, window, position, modifiers, time, new Vector2<double>(across, down)) : null;
        if (pan == null && vertical == 0 && horizontal == 0)
            return;

        var blocked = _modality.IsBlocked;
        if (blocked && (pan == null || IsKeptFrom(pan.Type)))
            return;

        Report(() =>
        {
            if (pan != null && PointerDevice.Current.ProcessTrackpad(pan) || blocked)
                return;

            if (vertical != 0)
                PointerDevice.Current.ProcessEvent(
                    new RawPointerWheelEventArgs(vertical, window, position, modifiers, time, false));
            if (horizontal != 0 && !_closed)
                PointerDevice.Current.ProcessEvent(
                    new RawPointerWheelEventArgs(horizontal, window, position, modifiers, time, true));
        });
    }

    // The end of a gesture still goes to a window a modal one has come over, as a release does.
    private bool IsKeptFrom(RawTrackpadEventType type) => _modality.IsBlocked && type != RawTrackpadEventType.Completed;

    // Null for a wheel, which has no phases. Fingers put down begin the pan already: they stop what coasts under them.
    private RawTrackpadEventArgs PanOf(IntPtr nsEvent, IWindow window, Vector2<double> position,
        InputModifiers modifiers, uint time, Vector2<double> delta)
    {
        var phase = NSEvent.PhaseOf(nsEvent);
        var type = NSEvent.MomentumPhaseOf(nsEvent) != NSEventPhase.None ? RawTrackpadEventType.Inertia
            : phase == NSEventPhase.MayBegin ? RawTrackpadEventType.Started
            : phase == NSEventPhase.Began && _fingersRest ? RawTrackpadEventType.Delta
            : TypeOf(phase);
        _fingersRest = phase == NSEventPhase.MayBegin;
        if (type is not { } known)
            return null;

        // With the fingers, whichever way the system scrolls.
        return new RawTrackpadEventArgs(known, ManipulationKinds.Pan, window, position, modifiers, time)
        {
            Translation = NSEvent.IsDirectionInvertedFromDevice(nsEvent) ? delta : -delta
        };
    }

    private static RawTrackpadEventType? TypeOf(NSEventPhase phase) => phase switch
    {
        NSEventPhase.Began => RawTrackpadEventType.Started,
        NSEventPhase.Changed or NSEventPhase.Stationary => RawTrackpadEventType.Delta,
        NSEventPhase.Ended or NSEventPhase.Cancelled => RawTrackpadEventType.Completed,
        _ => null
    };

    internal void OnMagnify(IntPtr nsEvent)
    {
        var window = _window;
        if (window == null || _closed || TypeOf(NSEvent.PhaseOf(nsEvent)) is not { } type || IsKeptFrom(type))
            return;

        var scale = Math.Max(1 + NSEvent.MagnificationOf(nsEvent), SmallestPinch);
        var args = new RawTrackpadEventArgs(type, ManipulationKinds.Pinch, window, PositionOf(nsEvent),
            Modifiers(nsEvent), TimeOf(nsEvent)) { Scale = scale };
        Report(() =>
        {
            if (!PointerDevice.Current.ProcessTrackpad(args))
                TurnWheelByPinch(args);
        });
    }

    // What takes no pinch is zoomed by it as by the wheel with Control: a whole notch for every tenth, as some step once for
    // every event, however small.
    private void TurnWheelByPinch(RawTrackpadEventArgs e)
    {
        if (e.Type == RawTrackpadEventType.Started)
            _pinchNotches = 0;

        _pinchNotches += Math.Log(e.Scale) / Math.Log(1.1);
        var notches = (int)Math.Truncate(_pinchNotches);
        if (notches == 0)
            return;

        _pinchNotches -= notches;
        PointerDevice.Current.ProcessEvent(new RawPointerWheelEventArgs(notches * WheelNotch, e.RootComponent, e.Position,
            e.InputModifiers | InputModifiers.LeftControl, e.Timestamp));
    }

    internal void OnRotate(IntPtr nsEvent)
    {
        var window = _window;
        if (window == null || _closed || TypeOf(NSEvent.PhaseOf(nsEvent)) is not { } type || IsKeptFrom(type))
            return;

        var args = new RawTrackpadEventArgs(type, ManipulationKinds.Rotate, window, PositionOf(nsEvent),
            Modifiers(nsEvent), TimeOf(nsEvent)) { Rotation = -NSEvent.RotationOf(nsEvent) };
        Report(() => PointerDevice.Current.ProcessTrackpad(args));
    }

    private void ReportDelta(IWindow window, IntPtr nsEvent)
    {
        var moved = new Vector2<double>(NSEvent.DeltaXOf(nsEvent), NSEvent.DeltaYOf(nsEvent)) * _scale;
        if (moved == Vector2<double>.Zero)
            return;

        var args = new RawMouseDeltaEventArgs(moved, window, Modifiers(nsEvent), TimeOf(nsEvent));
        Report(() => PointerDevice.Current.ProcessDelta(args));
    }

    internal void OnKey(IntPtr nsEvent, bool down)
    {
        var window = _window;
        if (window == null || _closed || _modality.IsBlocked)
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

        if (!down)
        {
            ReportKey(window, nsEvent, false, false);
            return;
        }

        if (IsEmojiShortcut(nsEvent, flags) && _takesText)
        {
            if (!NSEvent.IsRepeat(nsEvent))
                NSTextInputContext.ShowCharacters();
            return;
        }

        if (!_takesText || (flags & NSEventModifierFlags.Command) != 0 ||
            (flags & NSEventModifierFlags.Control) != 0 && !TextInput.IsComposing)
        {
            LeaveComposition();
            ReportKey(window, nsEvent, true, NSEvent.IsRepeat(nsEvent));
            ReportText(window, nsEvent, flags);
            return;
        }

        var context = NSTextInputContext.Of(_view);
        if (!TextInput.Press(() => NSTextInputContext.HandleEvent(context, nsEvent),
                () => ReportKey(window, nsEvent, true, NSEvent.IsRepeat(nsEvent))))
        {
            ReportText(window, nsEvent, flags);
        }
    }

    // Straight from the key, past the input method.
    private void ReportText(IWindow window, IntPtr nsEvent, NSEventModifierFlags flags)
    {
        if ((flags & (NSEventModifierFlags.Command | NSEventModifierFlags.Control)) != 0)
            return;

        var text = NSEvent.CharactersOf(nsEvent);
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0]) || (text[0] >= 0xF700 && text[0] <= 0xF8FF))
            return;

        var modifiers = Modifiers(nsEvent);
        var time = TimeOf(nsEvent);
        Report(() => KeyboardDevice.CurrentDevice.ProcessEvent(
            new RawTextInputEventArgs(text, modifiers, time), window));
    }

    /// <summary>What the input method makes of the keys; main thread.</summary>
    internal MacOSTextInput TextInput { get; private set; }

    // A letter held down where no text is typed - in a scene - repeats, with no accents offered for it.
    // Told on the loop thread, which the focus is on.
    private volatile bool _takesText;

    private void FindWhetherTextIsTaken() =>
        _takesText = CommandManager.EntryAround(FocusManager.Focused as IUIComponent) != null;

    private void OnFocusMoved(object sender, FocusMovedEventArgs e)
    {
        FindWhetherTextIsTaken();
        // What was composed was for where the focus was.
        OnMainThread(() => LeaveComposition(TextInput.Discard));
    }

    /// <summary>Where the input method puts its windows; main thread.</summary>
    internal CGRect CaretOnScreen
    {
        get
        {
            var caret = _window?.InputMethodCaret ?? default;
            var height = NSView.BoundsOf(_view).Size.Height;
            return NSTextInputContext.ToScreen(_view, new CGRect(caret.X, height - caret.Bottom, caret.Width, caret.Height));
        }
    }

    void ITextInputTarget.Type(string text)
    {
        var window = _window;
        Report(() => KeyboardDevice.CurrentDevice.ProcessEvent(
            new RawTextInputEventArgs(text, InputModifiers.None, Now), window));
    }

    void ITextInputTarget.Compose(RawTextCompositionEventType type, string text, int cursor)
    {
        // What is typed into a password is not shown.
        if (_hidesComposition)
            return;

        var window = _window;
        Report(() => KeyboardDevice.CurrentDevice.ProcessEvent(
            new RawTextCompositionEventArgs(type, text, cursor, InputModifiers.None, Now), window));
    }

    void ITextInputTarget.Erase(int count)
    {
        var window = _window;
        Report(() =>
        {
            for (var i = 0; i < count; i++)
            {
                KeyboardDevice.CurrentDevice.ProcessEvent(BackspaceOf(RawKeyboardEventType.KeyDown), window);
                KeyboardDevice.CurrentDevice.ProcessEvent(BackspaceOf(RawKeyboardEventType.KeyUp), window);
            }
        });
    }

    private static RawKeyboardEventArgs BackspaceOf(RawKeyboardEventType type) => new(Key.BackSpace, type,
        new KeyPressInfo { RepeatCount = 1 }, InputModifiers.None, Now);

    private static uint Now => unchecked((uint)Environment.TickCount64);

    // What is composed stays as it is when the caret is about to go elsewhere.
    private void LeaveComposition() => LeaveComposition(TextInput.Commit);

    private void LeaveComposition(Action end)
    {
        if (TextInput.IsComposing)
        {
            end();
            NSTextInputContext.DiscardMarkedText(NSTextInputContext.Of(_view));
        }

        TextInput.ForgetTyped();
    }

    // ⌃⌘F, as every Mac application has it.
    private bool IsFullScreenShortcut(IntPtr nsEvent, NSEventModifierFlags flags) =>
        IsHeldWithControlAndCommand(flags) && _platform.Keyboard.KeyOf(nsEvent) == Key.F;

    // ⌃⌘Space.
    private bool IsEmojiShortcut(IntPtr nsEvent, NSEventModifierFlags flags) =>
        IsHeldWithControlAndCommand(flags) && _platform.Keyboard.KeyOf(nsEvent) == Key.Space;

    private static bool IsHeldWithControlAndCommand(NSEventModifierFlags flags)
    {
        const NSEventModifierFlags shortcut = NSEventModifierFlags.Command | NSEventModifierFlags.Control;
        const NSEventModifierFlags held = shortcut | NSEventModifierFlags.Shift | NSEventModifierFlags.Option;
        return (flags & held) == shortcut;
    }

    internal void OnFlagsChanged(IntPtr nsEvent)
    {
        var window = _window;
        if (window == null || _closed)
            return;

        var held = HeldBy(MacOSKeys.ToKey(NSEvent.KeyCodeOf(nsEvent)));
        var down = (NSEvent.ModifierFlagsOf(nsEvent) & held) != 0;
        if (held != 0 && !(down && _modality.IsBlocked))
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
        var key = _platform.Keyboard.KeyOf(nsEvent, down);
        var type = down ? RawKeyboardEventType.KeyDown : RawKeyboardEventType.KeyUp;
        Report(() =>
        {
            KeyboardDevice.CurrentDevice.ProcessEvent(new RawKeyboardEventArgs(key, type, press, modifiers, time), window);
            // A web page goes from field to field within the focus.
            FindWhetherTextIsTaken();
        });
    }

    internal void OnCloseRequested()
    {
        if (!_modality.IsBlocked)
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
        // Another application gets its mouse back, whatever the window still thinks it looks by.
        if (isKey)
            EnterRelativeMouse();
        else
        {
            LeaveRelativeMouse();
            LeaveComposition();
        }

        // A window a modal one keeps from input is not the active one: the keyboard coming to it is handed on.
        if (isKey && _modality.IsBlocked)
        {
            _modality.ActivateBlocker();
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
        if (miniaturized)
            LeaveRelativeMouse();
        else
        {
            _number = NSWindow.NumberOf(_nsWindow);
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
        FocusManager.FocusMoved -= OnFocusMoved;
        _window.StateChanged -= WindowOnStateChanged;
        _window.ResizeModeChanged -= WindowOnResizeModeChanged;
        _platform.Unregister(this);
        _platform.Unregister(_modality);
        _modality.Close();

        Input.DragDrop.UnregisterNativeDropTarget(_window);
        UIContext.UIApplication.RemoveWindow(_window);
        _window.SetHandle(IntPtr.Zero);
        OnMainThread(() =>
        {
            LeaveRelativeMouse();
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
        Add(NSEventModifierFlags.Shift, NSEventModifierFlags.LeftShift, NSEventModifierFlags.RightShift,
            InputModifiers.LeftShift, InputModifiers.RightShift);
        Add(NSEventModifierFlags.Command, NSEventModifierFlags.LeftCommand, NSEventModifierFlags.RightCommand,
            InputModifiers.LeftControl, InputModifiers.RightControl);
        Add(NSEventModifierFlags.Control, NSEventModifierFlags.LeftControl, NSEventModifierFlags.RightControl,
            InputModifiers.LeftWindows, InputModifiers.RightWindows);
        Add(NSEventModifierFlags.Option, NSEventModifierFlags.LeftOption, NSEventModifierFlags.RightOption,
            InputModifiers.LeftAlt, InputModifiers.RightAlt);
        return modifiers;

        // An event that does not say which side - one made, not pressed - is the left one's.
        void Add(NSEventModifierFlags either, NSEventModifierFlags leftFlag, NSEventModifierFlags rightFlag,
            InputModifiers left, InputModifiers right)
        {
            if ((flags & either) == 0)
                return;

            if ((flags & rightFlag) != 0)
                modifiers |= right;
            if ((flags & leftFlag) != 0 || (flags & rightFlag) == 0)
                modifiers |= left;
        }
    }

    // Wraps after 49 days of the system up, as every platform's does: a double cast straight to uint would stay at the last.
    private static uint TimeOf(IntPtr nsEvent) => unchecked((uint)(ulong)(NSEvent.TimestampOf(nsEvent) * 1000));

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
