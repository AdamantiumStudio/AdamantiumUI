namespace Adamantium.UI.Core.Input;

/// <summary>The wheel turned under a pointer: by how much, and which way.</summary>
public class PointerWheelEventArgs : PointerEventArgs
{
    public PointerWheelEventArgs(Pointer pointer, InputModifiers modifiers, int delta, uint timestamp, bool isHorizontal = false)
        : base(pointer, modifiers, timestamp)
    {
        Delta = delta;
        IsHorizontal = isHorizontal;
    }

    /// <summary>Positive away from the user (up), in the OS's units of 120 a notch.</summary>
    public int Delta { get; }

    public bool IsHorizontal { get; }
}
