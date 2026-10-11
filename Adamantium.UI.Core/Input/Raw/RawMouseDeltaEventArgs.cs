using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input.Raw;

/// <summary>How far the mouse itself moved, unbound by the screen's edges - what a mouse-look camera turns by.</summary>
public class RawMouseDeltaEventArgs : RawInputEventArgs
{
    public RawMouseDeltaEventArgs(Vector2 delta, IInputComponent rootComponent, InputModifiers modifiers, uint timestamp)
        : base(modifiers, timestamp)
    {
        Delta = delta;
        RootComponent = rootComponent;
    }

    public Vector2 Delta { get; }

    public IInputComponent RootComponent { get; }
}
