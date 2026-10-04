using System;
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
