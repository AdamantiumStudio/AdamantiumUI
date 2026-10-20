namespace Adamantium.UI.Core.RoutedEvents;

public class StateChangedEventArgs : RoutedEventArgs
{
    public StateChangedEventArgs(WindowState state)
    {
        State = state;
    }
        
    public WindowState State { get; }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is StateChangedHandler typed)
        {
            typed(target, this);
            return;
        }

        base.InvokeEventHandler(handler, target);
    }
}