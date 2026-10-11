using Adamantium.Mathematics;
using Adamantium.UI.Core.Input.Raw;

namespace Adamantium.UI.Core.Input;

/// <summary>Turns what the platforms report about pointers into routed pointer events: hit-tests each pointer, keeps what
/// is under it and what holds it, counts its clicks. Every pointer has its own state, so two fingers press, drag and
/// hover two things at once.</summary>
public sealed class PointerDevice
{
    /// <summary>The mouse's pointer id.</summary>
    public const uint MouseId = 1;

    private readonly Dictionary<uint, Pointer> _pointers = new();

    // The finger lifted last, whose taps the next finger's are counted on from.
    private Pointer _lastTap;

    private PointerDevice()
    {
        MousePointer = new Pointer(MouseId, PointerType.Mouse, true);
        _pointers[MouseId] = MousePointer;
    }

    public static PointerDevice Current { get; } = new();

    /// <summary>The mouse. It is always there, whether or not anything moved it.</summary>
    public Pointer MousePointer { get; }

    /// <summary>Watches fingers and pens for a long press.</summary>
    internal HoldingRecognizer Holding { get; } = new();

    /// <summary>The pointers in use: the mouse, and every finger down and pen in range.</summary>
    public IReadOnlyCollection<Pointer> Pointers => _pointers.Values;

    /// <summary>Hands a platform's pointer event to the element it reaches. A finger and a pen's tip press the left
    /// button and a pen's barrel the right one, whatever the platform said: controls answer the left button, and a
    /// platform that forgot this would leave every one of them deaf to touch.</summary>
    public void ProcessEvent(RawPointerEventArgs e)
    {
        var pointer = PointerFor(e);
        var (button, properties) = ContactButton(e, pointer);
        pointer.ScreenPosition = e.RootComponent.PointToScreen(e.Position);
        pointer.PositionRoot = e.RootComponent;
        pointer.Properties = properties;
        pointer.IsPrimary = e.IsPrimary;

        switch (e.EventType)
        {
            case RawPointerEventType.Move:
                pointer.HoverRoot = e.RootComponent;
                Move(pointer, e.RootComponent, e.Position, e.InputModifiers, e.Timestamp);
                break;

            case RawPointerEventType.LeaveWindow:
                Leave(pointer, e.RootComponent, e.InputModifiers, e.Timestamp);
                break;

            case RawPointerEventType.Down:
                Down(pointer, e.RootComponent, e.Position, button, e.InputModifiers, e.Timestamp);
                break;

            case RawPointerEventType.Up:
                Up(pointer, e.RootComponent, e.Position, button, e.InputModifiers, e.Timestamp);
                break;

            case RawPointerEventType.Wheel:
                var wheel = (RawPointerWheelEventArgs)e;
                Wheel(pointer, e.RootComponent, e.Position, e.InputModifiers, e.Timestamp, wheel.WheelDelta, wheel.IsHorizontal);
                break;

            case RawPointerEventType.Cancel:
                Cancel(pointer, e.InputModifiers, e.Timestamp);
                break;
        }
    }

    /// <summary>Hands how far the mouse itself moved to the focused element.</summary>
    public void ProcessDelta(RawMouseDeltaEventArgs e)
    {
        FocusManager.Focused?.RaiseEvent(new UnboundMouseEventArgs(MousePointer, e.Delta, e.InputModifiers, e.Timestamp)
        {
            RoutedEvent = Mouse.RawMouseMoveEvent
        });
    }

    /// <summary>Mirrors the mouse's capture to the OS after a mouse event, so a drag keeps its moves and release off the
    /// window. <paramref name="osCaptured"/> is the platform's own flag; the call fires only on a change.</summary>
    public void SyncOsMouseCapture(IWindowWorkerService worker, ref bool osCaptured)
    {
        var want = MousePointer.Captured != null;
        if (want == osCaptured)
        {
            return;
        }

        osCaptured = want;
        worker.SetMouseCapture(want);
    }

    /// <summary>Feeds the mouse's position from an OS-driven drag, where another application owns the mouse and no move
    /// reaches us. Call on the UI loop thread.</summary>
    public void SetExternalMousePosition(IInputComponent root, PixelPoint screenPoint)
    {
        MousePointer.ScreenPosition = screenPoint;
        MousePointer.PositionRoot = root;
    }

    /// <summary>Re-decides what is under a still mouse when the content under it has moved (a list scrolled by keyboard
    /// or wheel). Call once layout has settled; ignored while something holds the mouse.</summary>
    public void RefreshPointerOver(IInputComponent root)
    {
        if (MousePointer.Captured != null || root is not IRootVisualComponent client)
        {
            return;
        }

        if (!ReferenceEquals(root, MousePointer.HoverRoot))
        {
            return;
        }

        SetPointerOver(MousePointer, root, client.PointToClient(MousePointer.ScreenPosition), InputModifiers.None, 0);
    }

    /// <summary>What a pointer is over at <paramref name="p"/> (client units) of <paramref name="rootComponent"/>: open
    /// popups first, newest on top, then the window content - the element a press there reaches.</summary>
    public static IInputComponent HitTestTopmost(IInputComponent rootComponent, Vector2 p, bool boundsForContent = false)
    {
        if (rootComponent is IWindow window)
        {
            var popups = window.PopupRoots;
            for (var i = popups.Count - 1; i >= 0; i--)
            {
                if (!popups[i].IsHitTestVisible)
                {
                    continue;
                }

                if (InputExtensions.HitTest(popups[i], p) is { } popupHit)
                {
                    return popupHit;
                }

                if (popups[i].ClipRectangle.Contains(p))
                {
                    return popups[i] as IInputComponent;
                }
            }
        }

        var pixel = InputExtensions.HitTest(rootComponent, p);
        if (!boundsForContent)
        {
            return pixel;
        }

        var bounds = InputExtensions.HitTestBounds(rootComponent, p);
        if (pixel == null)
        {
            return bounds;
        }

        if (bounds != null && !ReferenceEquals(pixel, bounds) && IsAncestorOf(pixel, bounds))
        {
            return bounds;
        }

        return pixel;
    }

    // The button a contact presses, and the buttons it holds. A mouse says its own. A finger presses the left button as
    // it touches; a pen's tip the left one, or the right one with its side button held as it touches. The pointer
    // keeps that button until the contact lifts, so a press and its lift agree and a hovering contact holds none.
    private static (MouseButtons Button, PointerPointProperties Properties) ContactButton(RawPointerEventArgs e, Pointer pointer)
    {
        if (e.PointerType == PointerType.Mouse)
        {
            return (e.ChangedButton, e.Properties);
        }

        var button = MouseButtons.None;
        switch (e.EventType)
        {
            case RawPointerEventType.Down:
                pointer.ContactButton = e.PointerType == PointerType.Pen && e.Properties.IsBarrelButtonPressed
                    ? MouseButtons.Right
                    : MouseButtons.Left;
                button = pointer.ContactButton;
                break;
            case RawPointerEventType.Up:
                button = pointer.ContactButton == MouseButtons.None ? MouseButtons.Left : pointer.ContactButton;
                pointer.ContactButton = MouseButtons.None;
                break;
            case RawPointerEventType.Cancel or RawPointerEventType.LeaveWindow:
                pointer.ContactButton = MouseButtons.None;
                break;
        }

        var held = pointer.ContactButton;
        var properties = e.Properties with
        {
            IsLeftButtonPressed = held == MouseButtons.Left,
            IsRightButtonPressed = held == MouseButtons.Right,
            IsMiddleButtonPressed = false,
            IsXButton1Pressed = false,
            IsXButton2Pressed = false
        };

        return (button, properties);
    }

    private Pointer PointerFor(RawPointerEventArgs e)
    {
        if (_pointers.TryGetValue(e.PointerId, out var pointer))
        {
            return pointer;
        }

        pointer = new Pointer(e.PointerId, e.PointerType, e.IsPrimary);
        if (e.PointerType == PointerType.Touch && _lastTap != null)
        {
            // Every touch is a new pointer, so a double tap is counted from the finger lifted just before.
            pointer.ClickCount = _lastTap.ClickCount;
            pointer.LastClickTime = _lastTap.LastClickTime;
            pointer.LastClicked = _lastTap.LastClicked;
            pointer.LastClickPosition = _lastTap.LastClickPosition;
        }

        _pointers[e.PointerId] = pointer;
        return pointer;
    }

    private static bool IsAncestorOf(IInputComponent ancestor, IInputComponent node)
    {
        for (var parent = (node as IUIComponent)?.VisualParent; parent != null; parent = parent.VisualParent)
        {
            if (ReferenceEquals(parent, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void Leave(Pointer pointer, IInputComponent root, InputModifiers modifiers, uint timestamp)
    {
        if (ReferenceEquals(pointer.HoverRoot, root))
        {
            pointer.HoverRoot = null;
        }

        Holding.Cancel(pointer, timestamp);
        ChangePointerOver(pointer, null, modifiers, timestamp);
        if (pointer.Type != PointerType.Mouse && pointer.Captured == null)
        {
            _pointers.Remove(pointer.Id);
        }
    }

    private void ChangePointerOver(Pointer pointer, IInputComponent newOver, InputModifiers modifiers, uint timestamp)
    {
        var previous = pointer.DirectlyOver;
        AncestorState.Transition(previous, newOver, PointerEvents.PointerEnteredEvent, PointerEvents.PointerExitedEvent,
            evt => new PointerEventArgs(pointer, modifiers, timestamp) { RoutedEvent = evt });

        if (ReferenceEquals(previous, newOver))
        {
            return;
        }

        pointer.DirectlyOver = newOver;
        previous?.RaiseEvent(new PointerEventArgs(pointer, modifiers, timestamp) { RoutedEvent = PointerEvents.DirectlyOverExitedEvent });
        newOver?.RaiseEvent(new PointerEventArgs(pointer, modifiers, timestamp) { RoutedEvent = PointerEvents.DirectlyOverEnteredEvent });
    }

    private IInputComponent SetPointerOver(Pointer pointer, IInputComponent root, Vector2 p, InputModifiers modifiers, uint timestamp)
    {
        var element = HitTestTopmost(root, p, boundsForContent: true) ?? root;
        ChangePointerOver(pointer, element, modifiers, timestamp);
        return pointer.DirectlyOver;
    }

    private void Move(Pointer pointer, IInputComponent root, Vector2 p, InputModifiers modifiers, uint timestamp)
    {
        IInputComponent source;
        if (pointer.Captured == null)
        {
            source = SetPointerOver(pointer, root, p, modifiers, timestamp);
        }
        else
        {
            ChangePointerOver(pointer, pointer.Captured.HitTest(p) ?? pointer.Captured, modifiers, timestamp);
            source = pointer.Captured;
        }

        var args = new PointerEventArgs(pointer, modifiers, timestamp) { RoutedEvent = PointerEvents.PreviewPointerMovedEvent };
        source.RaiseEvent(args);
        args.RoutedEvent = PointerEvents.PointerMovedEvent;
        source.RaiseEvent(args);
        Holding.Move(pointer, timestamp);
    }

    private void Down(Pointer pointer, IInputComponent root, Vector2 p, MouseButtons button, InputModifiers modifiers, uint timestamp)
    {
        if (pointer.Type == PointerType.Touch && pointer.Captured == null)
        {
            SetPointerOver(pointer, root, p, modifiers, timestamp);
        }

        var hit = pointer.Captured ?? HitTestTopmost(root, p);
        if (hit == null)
        {
            return;
        }

        var box = PlatformSettings.DoubleClickSize;
        var moved = Math.Abs(p.X - pointer.LastClickPosition.X) > box.Width ||
                    Math.Abs(p.Y - pointer.LastClickPosition.Y) > box.Height;
        if (timestamp - pointer.LastClickTime > PlatformSettings.DoubleClickTime || pointer.LastClicked != hit || moved)
        {
            pointer.ClickCount = 0;
        }

        pointer.ClickCount++;
        pointer.LastClickTime = timestamp;
        pointer.LastClicked = hit;
        pointer.LastClickPosition = p;

        var args = new PointerPressedEventArgs(pointer, button, modifiers, timestamp)
        {
            RoutedEvent = PointerEvents.PreviewPointerPressedEvent,
            ClickCount = pointer.ClickCount
        };
        hit.RaiseEvent(args);
        args.RoutedEvent = PointerEvents.PointerPressedEvent;
        hit.RaiseEvent(args);
        Holding.Down(pointer, hit, button, modifiers, timestamp);
    }

    private void Up(Pointer pointer, IInputComponent root, Vector2 p, MouseButtons button, InputModifiers modifiers, uint timestamp)
    {
        var hit = pointer.Captured ?? HitTestTopmost(root, p);
        if (hit != null)
        {
            var args = new PointerReleasedEventArgs(pointer, button, modifiers, timestamp)
            {
                RoutedEvent = PointerEvents.PreviewPointerReleasedEvent
            };
            hit.RaiseEvent(args);
            args.RoutedEvent = PointerEvents.PointerReleasedEvent;
            hit.RaiseEvent(args);
        }

        // After the release, so what answers it still sees the pointer holding and takes the lift for a long press.
        Holding.Up(pointer, timestamp);
        if (pointer.Type == PointerType.Touch)
        {
            Lift(pointer, modifiers, timestamp);
        }
    }

    private void Cancel(Pointer pointer, InputModifiers modifiers, uint timestamp)
    {
        (pointer.Captured ?? pointer.DirectlyOver)?.RaiseEvent(
            new PointerEventArgs(pointer, modifiers, timestamp) { RoutedEvent = PointerEvents.PointerCanceledEvent });
        Holding.Cancel(pointer, timestamp);

        if (pointer.Type == PointerType.Mouse)
        {
            pointer.Capture(null);
            return;
        }

        Lift(pointer, modifiers, timestamp);
    }

    private void Lift(Pointer pointer, InputModifiers modifiers, uint timestamp)
    {
        pointer.Capture(null);
        ChangePointerOver(pointer, null, modifiers, timestamp);
        _pointers.Remove(pointer.Id);
        if (pointer.Type == PointerType.Touch)
        {
            _lastTap = pointer;
        }
    }

    private void Wheel(Pointer pointer, IInputComponent root, Vector2 p, InputModifiers modifiers, uint timestamp, int delta,
        bool isHorizontal)
    {
        var hit = HitTestTopmost(root, p);
        if (hit == null)
        {
            return;
        }

        var args = new PointerWheelEventArgs(pointer, modifiers, delta, timestamp, isHorizontal)
        {
            RoutedEvent = PointerEvents.PreviewPointerWheelChangedEvent
        };
        hit.RaiseEvent(args);
        args.RoutedEvent = PointerEvents.PointerWheelChangedEvent;
        hit.RaiseEvent(args);
    }
}
