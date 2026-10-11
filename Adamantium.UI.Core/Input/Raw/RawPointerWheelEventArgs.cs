using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input.Raw;

/// <summary>The wheel turned, as a platform reports it.</summary>
public class RawPointerWheelEventArgs : RawPointerEventArgs
{
    public RawPointerWheelEventArgs(int wheelDelta, IInputComponent rootComponent, Vector2 position,
        InputModifiers modifiers, uint timestamp, bool isHorizontal = false)
        : base(RawPointerEventType.Wheel, rootComponent, position, modifiers, timestamp)
    {
        WheelDelta = wheelDelta;
        IsHorizontal = isHorizontal;
    }

    public int WheelDelta { get; }

    public bool IsHorizontal { get; }
}
