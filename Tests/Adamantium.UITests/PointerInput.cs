using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UITests;

/// <summary>The mouse's events raised straight on an element, for tests where nothing hit-tests: each one carries the
/// buttons it says are held, as a real one carries what the OS reported.</summary>
internal static class PointerInput
{
    public static void Press(IInputComponent target, MouseButtons button = MouseButtons.Left,
        InputModifiers modifiers = InputModifiers.None, int clickCount = 1) =>
        RaisePress(target, PointerEvents.PointerPressedEvent, button, modifiers, clickCount);

    public static void PreviewPress(IInputComponent target, MouseButtons button = MouseButtons.Left) =>
        RaisePress(target, PointerEvents.PreviewPointerPressedEvent, button, InputModifiers.None, 1);

    public static void Release(IInputComponent target, MouseButtons button = MouseButtons.Left) =>
        target.RaiseEvent(new PointerReleasedEventArgs(Mouse.Pointer, button, InputModifiers.None, 0)
        {
            RoutedEvent = PointerEvents.PointerReleasedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });

    public static void Move(IInputComponent target, InputModifiers held = InputModifiers.None) =>
        target.RaiseEvent(new PointerEventArgs(Mouse.Pointer, held, 0)
        {
            RoutedEvent = PointerEvents.PointerMovedEvent,
            Properties = PointerPointProperties.FromMouse(held)
        });

    public static void Enter(IInputComponent target) =>
        target.RaiseEvent(new PointerEventArgs(Mouse.Pointer, InputModifiers.None, 0) { RoutedEvent = PointerEvents.PointerEnteredEvent });

    public static void Exit(IInputComponent target) =>
        target.RaiseEvent(new PointerEventArgs(Mouse.Pointer, InputModifiers.None, 0) { RoutedEvent = PointerEvents.PointerExitedEvent });

    private static void RaisePress(IInputComponent target, Adamantium.UI.Core.RoutedEvents.RoutedEvent routedEvent,
        MouseButtons button, InputModifiers modifiers, int clickCount)
    {
        var held = modifiers | Held(button);
        target.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, button, held, 0)
        {
            RoutedEvent = routedEvent,
            Properties = PointerPointProperties.FromMouse(held),
            ClickCount = clickCount
        });
    }

    private static InputModifiers Held(MouseButtons button) => button switch
    {
        MouseButtons.Left => InputModifiers.LeftMouseButton,
        MouseButtons.Right => InputModifiers.RightMouseButton,
        MouseButtons.Middle => InputModifiers.MiddleMouseButton,
        MouseButtons.XButton1 => InputModifiers.X1MouseButton,
        MouseButtons.XButton2 => InputModifiers.X2MouseButton,
        _ => InputModifiers.None
    };
}
