using System;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Platforms;

namespace Adamantium.UI.Automation;

internal static class InputSimulator
{
    public static void Click(UIComponent element, string label, MouseButtons button)
    {
        using var pointer = SimulatedPointer.Install();
        var (window, point) = HoverAt(element, label, Middle(element));
        var (down, up, held) = button == MouseButtons.Right
            ? (RawMouseEventType.RightButtonDown, RawMouseEventType.RightButtonUp, InputModifiers.RightMouseButton)
            : (RawMouseEventType.LeftButtonDown, RawMouseEventType.LeftButtonUp, InputModifiers.LeftMouseButton);

        Send(down, window, point, held);
        Send(up, window, point, InputModifiers.None);
    }

    public static void Hover(UIComponent element, string label)
    {
        using var pointer = SimulatedPointer.Install();
        HoverAt(element, label, Middle(element));
    }

    public static void Drag(UIComponent element, string label, Vector2 from, Vector2 to)
    {
        using var pointer = SimulatedPointer.Install();
        var (window, start) = HoverAt(element, label, from);
        Stroke(window, start, InWindow(element, to));
    }

    /// <summary>Takes <paramref name="source"/> at <paramref name="from"/> and lets it go over <paramref name="at"/> of
    /// <paramref name="target"/> - a point that has to be on <paramref name="within"/> - as a hand carrying one onto
    /// the other would. The point is where the target stands before anything moves: a list that opens a gap for the
    /// carried row places it by that, not by where the gap pushes its rows.</summary>
    public static void DragOnto(UIComponent source, string sourceLabel, Vector2 from, UIComponent target, string targetLabel,
        Vector2 at, UIComponent within)
    {
        if (source.RootVisual is not IUIComponent root || target.RootVisual is not IWindow targetWindow)
        {
            throw new AutomationException($"{sourceLabel} or {targetLabel} is not in a window.");
        }

        if (!Reaches(target, at, within))
        {
            var point = InWindow(target, at);
            throw new AutomationException(
                $"{targetLabel} cannot be reached at ({point.X:0}, {point.Y:0}): it is off screen or covered - by another window, too.");
        }

        const int steps = 8;
        using var pointer = SimulatedPointer.Install();
        var (window, start) = HoverAt(source, sourceLabel, from);

        // The events stay with the window the gesture began in, as a captured pointer's do; a target in another window is
        // reached by the same desktop point, said in this window's terms, and the pointer is over that window once the
        // point is in it - which the desktop cannot say when another application's window lies over both.
        var end = ReferenceEquals(targetWindow, window)
            ? InWindow(target, at)
            : window.PointToClient(targetWindow.PointToScreen(InWindow(target, at)));
        IWindow Over(Vector2 point) => !ReferenceEquals(targetWindow, window) && Holds(targetWindow, window.PointToScreen(point))
            ? targetWindow
            : window;

        var layout = LayoutManager.GetOrCreate(root);
        Send(RawMouseEventType.LeftButtonDown, window, start, InputModifiers.LeftMouseButton);
        Send(RawMouseEventType.MouseMove, window, LeadIn(start, end), InputModifiers.LeftMouseButton);
        for (var step = 1; step <= steps; step++)
        {
            // A layout pass between moves, as a frame would come between them.
            layout.ExecuteLayoutPass();
            var point = start + (end - start) * (step / (double)steps);
            Send(RawMouseEventType.MouseMove, window, point, InputModifiers.LeftMouseButton, Over(point));
        }

        Send(RawMouseEventType.LeftButtonUp, window, end, InputModifiers.None, Over(end));
    }

    private static bool Holds(IWindow window, PixelPoint screen)
    {
        var client = window.PointToClient(screen);
        return client.X >= 0 && client.Y >= 0 && client.X <= window.ClientWidth && client.Y <= window.ClientHeight;
    }

    // The first few pixels of a hand's stroke, still over what it pressed: a drag starts once the pointer has gone past
    // the threshold over its source, and a first step an eighth of a long way across would leave the source before that.
    private static Vector2 LeadIn(Vector2 start, Vector2 end)
    {
        var way = end - start;
        var length = way.Length();
        return length <= 8 ? end : start + way * (8 / length);
    }

    private static void Stroke(IWindow window, Vector2 start, Vector2 end)
    {
        const int steps = 8;
        Send(RawMouseEventType.LeftButtonDown, window, start, InputModifiers.LeftMouseButton);
        Send(RawMouseEventType.MouseMove, window, LeadIn(start, end), InputModifiers.LeftMouseButton);
        for (var step = 1; step <= steps; step++)
        {
            var at = start + (end - start) * (step / (double)steps);
            Send(RawMouseEventType.MouseMove, window, at, InputModifiers.LeftMouseButton);
        }

        Send(RawMouseEventType.LeftButtonUp, window, end, InputModifiers.None);
    }

    /// <summary>Whether a pointer at <paramref name="local"/> of <paramref name="element"/> would be on
    /// <paramref name="within"/> - on screen, and not under something else, another of the application's windows
    /// included.</summary>
    public static bool Reaches(UIComponent element, Vector2 local, UIComponent within)
    {
        if (element.RootVisual is not IWindow window || window is not IUIComponent root)
        {
            return false;
        }

        var point = InWindow(element, local);
        if (!IsWithin(root.HitTest(point) as IUIComponent, within))
        {
            return false;
        }

        var app = UIApplication.Current;
        if (app?.Container is not { } container || !container.IsRegistered<IApplicationPlatform>())
        {
            return true;
        }

        var top = container.Resolve<IApplicationPlatform>().WindowFromScreenPoint(window.PointToScreen(point));
        return app.Windows.FirstOrDefault(w => w.Handle == top) is not { } ours || ReferenceEquals(ours, window);
    }

    private static (IWindow Window, Vector2 Point) HoverAt(UIComponent element, string label, Vector2 local)
    {
        if (element.RootVisual is not IWindow window)
        {
            throw new AutomationException($"{label} is not in a window.");
        }

        var point = InWindow(element, local);
        Send(RawMouseEventType.MouseMove, window, point, InputModifiers.None);
        var over = MouseDevice.CurrentDevice.DirectlyOver;
        if (!IsWithin(over, element))
        {
            throw new AutomationException(
                $"{label} is covered at ({point.X:0}, {point.Y:0}) by {over?.GetType().Name ?? "nothing"}; the pointer did not reach it.");
        }

        return (window, point);
    }

    public static void Type(string text)
    {
        foreach (var character in text)
        {
            KeyboardDevice.CurrentDevice.ProcessEvent(
                new RawTextInputEventArgs(character.ToString(), InputModifiers.None, Now()));
        }
    }

    public static void Press(string chord, IInputComponent window)
    {
        var keys = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (keys.Length == 0)
        {
            throw new AutomationException("No key named.");
        }

        var held = InputModifiers.None;
        foreach (var name in keys[..^1])
        {
            var modifier = KeyOf(name);
            var flag = ModifierOf(modifier) ?? throw new AutomationException($"'{name}' in '{chord}' is not Ctrl, Alt or Shift.");
            SendKey(modifier, RawKeyboardEventType.KeyDown, held, window);
            held |= flag;
        }

        var key = KeyOf(keys[^1]);
        SendKey(key, RawKeyboardEventType.KeyDown, held, window);
        if (CharacterOf(key, held) is { } character)
        {
            KeyboardDevice.CurrentDevice.ProcessEvent(new RawTextInputEventArgs(character.ToString(), held, Now()), window);
        }

        SendKey(key, RawKeyboardEventType.KeyUp, held, window);
        foreach (var name in keys[..^1].Reverse())
        {
            var modifier = KeyOf(name);
            held &= ~ModifierOf(modifier).Value;
            SendKey(modifier, RawKeyboardEventType.KeyUp, held, window);
        }
    }

    private static Key KeyOf(string name) => name.ToLowerInvariant() switch
    {
        "ctrl" or "control" => Key.Ctrl,
        "alt" => Key.Alt,
        "shift" => Key.Shift,
        "esc" => Key.Escape,
        "up" => Key.UpArrow,
        "down" => Key.DownArrow,
        "left" => Key.LeftArrow,
        "right" => Key.RightArrow,
        _ when name.Length == 1 && char.IsDigit(name[0]) => (Key)((int)Key.D0 + name[0] - '0'),
        _ => Enum.TryParse<Key>(name, true, out var key)
            ? key
            : throw new AutomationException($"'{name}' is not a key: a letter, a digit, Enter, Tab, Escape, F1, Ctrl, Alt...")
    };

    private static InputModifiers? ModifierOf(Key key) => key switch
    {
        Key.Ctrl or Key.LeftCtrl => InputModifiers.LeftControl,
        Key.Alt or Key.LeftAlt => InputModifiers.LeftAlt,
        Key.Shift or Key.LeftShift => InputModifiers.LeftShift,
        _ => null
    };

    private static char? CharacterOf(Key key, InputModifiers held)
    {
        if ((held & (InputModifiers.LeftControl | InputModifiers.LeftAlt)) != 0)
        {
            return null;
        }

        var shifted = (held & InputModifiers.LeftShift) != 0;
        return key switch
        {
            >= Key.A and <= Key.Z => (char)((shifted ? 'A' : 'a') + ((int)key - (int)Key.A)),
            >= Key.D0 and <= Key.D9 when !shifted => (char)('0' + ((int)key - (int)Key.D0)),
            Key.Space => ' ',
            _ => null
        };
    }

    private static void SendKey(Key key, RawKeyboardEventType type, InputModifiers modifiers, IInputComponent window)
    {
        var press = new KeyPressInfo { PreviousState = type == RawKeyboardEventType.KeyDown ? KeyState.Up : KeyState.Down };
        KeyboardDevice.CurrentDevice.ProcessEvent(new RawKeyboardEventArgs(key, type, press, modifiers, Now()), window);
    }

    // The event goes to window; the pointer is over the window named, another one only while a captured gesture is over it.
    private static void Send(RawMouseEventType type, IWindow window, Vector2 point, InputModifiers modifiers, IWindow over = null)
    {
        var device = MouseDevice.CurrentDevice;
        if (Mouse.Platform is SimulatedPointer pointer)
        {
            pointer.Position = window.PointToScreen(point);
            pointer.Window = over ?? window;
        }

        device.ProcessEvent(new RawMouseEventArgs(type, (IInputComponent)window, point, modifiers, device, Now()));
    }

    private static Vector2 Middle(UIComponent element) =>
        new(element.RenderSize.Width / 2, element.RenderSize.Height / 2);

    private static Vector2 InWindow(UIComponent element, Vector2 local)
    {
        var point = Vector3F.TransformCoordinate(new Vector3F((float)local.X, (float)local.Y, 0), element.WorldTransform);
        return new Vector2(point.X, point.Y);
    }

    private static bool IsWithin(IUIComponent node, IUIComponent element)
    {
        for (; node != null; node = node.VisualParent)
        {
            if (ReferenceEquals(node, element))
            {
                return true;
            }
        }

        return false;
    }

    private static uint Now() => (uint)Environment.TickCount;
}
