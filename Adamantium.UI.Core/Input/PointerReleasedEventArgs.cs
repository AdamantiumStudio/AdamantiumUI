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

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is PointerReleasedEventHandler typed)
        {
            typed(target, this);
            return;
        }

        base.InvokeEventHandler(handler, target);
    }
}
