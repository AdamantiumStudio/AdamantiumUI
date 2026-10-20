using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

/// <summary>How far the mouse itself moved, unbound by the screen's edges.</summary>
public class UnboundMouseEventArgs : PointerEventArgs
{
    public UnboundMouseEventArgs(Pointer pointer, Vector2 mouseDelta, InputModifiers modifiers, uint timestamp)
        : base(pointer, modifiers, timestamp)
    {
        Delta = mouseDelta;
    }

    public Vector2 Delta { get; }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is RawMouseEventHandler typed)
        {
            typed(target, this);
            return;
        }

        base.InvokeEventHandler(handler, target);
    }
}
