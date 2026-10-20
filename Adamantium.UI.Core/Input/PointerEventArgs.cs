using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

/// <summary>A pointer event: which pointer, and what it reported when the event was raised.</summary>
public class PointerEventArgs : InputEventArgs
{
    public PointerEventArgs(Pointer pointer, InputModifiers modifiers, uint timestamp) : base(modifiers, timestamp)
    {
        Pointer = pointer;
        Properties = pointer.Properties;
    }

    public Pointer Pointer { get; }

    /// <summary>What the pointer reported for this event: buttons, pressure, tilt. The pointer's own by default; an event
    /// raised from code says its own.</summary>
    public PointerPointProperties Properties { get; init; }

    /// <summary>Where the pointer is, in <paramref name="relativeTo"/>'s own units.</summary>
    public Vector2 GetPosition(IInputComponent relativeTo) => Pointer.GetPosition(relativeTo);

    /// <summary>The pointer in <paramref name="relativeTo"/>'s units, with what it reported for this event.</summary>
    public PointerPoint GetCurrentPoint(IInputComponent relativeTo) => new(Pointer, GetPosition(relativeTo), Properties);

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is PointerEventHandler typed)
        {
            typed(target, this);
            return;
        }

        base.InvokeEventHandler(handler, target);
    }
}
