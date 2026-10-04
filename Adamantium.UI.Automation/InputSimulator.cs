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
        if (element.RootVisual is not IWindow window)
        {
            throw new AutomationException($"{label} is not in a window.");
        }

        var size = element.RenderSize;
        var middle = Vector3F.TransformCoordinate(
            new Vector3F((float)(size.Width / 2), (float)(size.Height / 2), 0), element.WorldTransform);
        var point = new Vector2(middle.X, middle.Y);

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
