using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input.Raw;

/// <summary>A pointer event as a platform reports it, before it is hit-tested and routed.</summary>
public class RawPointerEventArgs : RawInputEventArgs
{
    public RawPointerEventArgs(RawPointerEventType eventType, IInputComponent rootComponent, Vector2<double> position,
        InputModifiers modifiers, uint timestamp) : base(modifiers, timestamp)
    {
        EventType = eventType;
        RootComponent = rootComponent;
        Position = position;
        Properties = PointerPointProperties.FromMouse(modifiers);
    }

    public RawPointerEventType EventType { get; }

    /// <summary>The window the event arrived in.</summary>
    public IInputComponent RootComponent { get; }

    /// <summary>Where the pointer is, in the window's logical client units.</summary>
    public Vector2<double> Position { get; }

    /// <summary>The OS's id for the pointer; the mouse is 1.</summary>
    public uint PointerId { get; init; } = PointerDevice.MouseId;

    public PointerType PointerType { get; init; } = PointerType.Mouse;

    public bool IsPrimary { get; init; } = true;

    /// <summary>The button that went down or up, for <see cref="RawPointerEventType.Down"/> and
    /// <see cref="RawPointerEventType.Up"/>.</summary>
    public MouseButtons ChangedButton { get; init; }

    /// <summary>What the pointer reports: buttons, pressure, tilt. A mouse's come from its modifiers.</summary>
    public PointerPointProperties Properties { get; init; }
}
