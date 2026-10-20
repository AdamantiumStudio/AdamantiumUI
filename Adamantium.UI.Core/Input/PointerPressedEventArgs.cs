namespace Adamantium.UI.Core.Input;

/// <summary>A pointer pressed: which button (a finger or a pen tip presses the left one), and how many presses in a row
/// it is.</summary>
public class PointerPressedEventArgs : PointerEventArgs
{
    public PointerPressedEventArgs(Pointer pointer, MouseButtons changedButton, InputModifiers modifiers, uint timestamp)
        : base(pointer, modifiers, timestamp)
    {
        ChangedButton = changedButton;
    }

    public MouseButtons ChangedButton { get; }

    /// <summary>1 for a single press, 2 for a double one, and so on.</summary>
    public int ClickCount { get; internal set; } = 1;

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is PointerPressedEventHandler typed)
        {
            typed(target, this);
            return;
        }

        base.InvokeEventHandler(handler, target);
    }
}
