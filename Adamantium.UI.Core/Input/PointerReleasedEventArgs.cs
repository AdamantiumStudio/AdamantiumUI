namespace Adamantium.UI.Core.Input;

/// <summary>A pointer released: which button let go.</summary>
public class PointerReleasedEventArgs : PointerEventArgs
{
    public PointerReleasedEventArgs(Pointer pointer, MouseButtons changedButton, InputModifiers modifiers, uint timestamp)
        : base(pointer, modifiers, timestamp)
    {
        ChangedButton = changedButton;
    }

    public MouseButtons ChangedButton { get; }
}
