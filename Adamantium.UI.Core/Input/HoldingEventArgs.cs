namespace Adamantium.UI.Core.Input;

/// <summary>A finger or a pen held still on an element: which state the hold is in.</summary>
public class HoldingEventArgs : PointerEventArgs
{
    public HoldingEventArgs(Pointer pointer, HoldingState holdingState, InputModifiers modifiers, uint timestamp)
        : base(pointer, modifiers, timestamp)
    {
        HoldingState = holdingState;
    }

    public HoldingState HoldingState { get; }
}

public delegate void HoldingEventHandler(object sender, HoldingEventArgs e);
