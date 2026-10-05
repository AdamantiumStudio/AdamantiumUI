using System;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;

namespace Adamantium.UI.Automation;

internal static class InputSimulator
{
    public static void Click(UIComponent element, string label, MouseButtons button)
    {
        var (window, point) = Hover(element, label);
        var (down, up, held) = button == MouseButtons.Right
            ? (RawMouseEventType.RightButtonDown, RawMouseEventType.RightButtonUp, InputModifiers.RightMouseButton)
            : (RawMouseEventType.LeftButtonDown, RawMouseEventType.LeftButtonUp, InputModifiers.LeftMouseButton);

        Send(down, window, point, held);
        Send(up, window, point, InputModifiers.None);
    }

    public static (IWindow Window, Vector2 Point) Hover(UIComponent element, string label)
    {
        var size = element.RenderSize;
        return HoverAt(element, label, new Vector2(size.Width / 2, size.Height / 2));
    }

    public static void Drag(UIComponent element, string label, Vector2 from, Vector2 to)
    {
        const int steps = 8;
        var (window, start) = HoverAt(element, label, from);
        var end = InWindow(element, to);
        Send(RawMouseEventType.LeftButtonDown, window, start, InputModifiers.LeftMouseButton);
        for (var step = 1; step <= steps; step++)
        {
            var at = start + (end - start) * (step / (double)steps);
            Send(RawMouseEventType.MouseMove, window, at, InputModifiers.LeftMouseButton);
        }

        Send(RawMouseEventType.LeftButtonUp, window, end, InputModifiers.None);
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

    private static void Send(RawMouseEventType type, IWindow window, Vector2 point, InputModifiers modifiers)
    {
        var device = MouseDevice.CurrentDevice;
        device.ProcessEvent(new RawMouseEventArgs(type, (IInputComponent)window, point, modifiers, device, Now()));
    }

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
