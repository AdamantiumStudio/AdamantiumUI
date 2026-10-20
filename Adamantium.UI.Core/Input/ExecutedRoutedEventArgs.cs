using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

/// <summary>A <see cref="RoutedCommand"/> run on its way up from its target; the element that runs it handles it.</summary>
public class ExecutedRoutedEventArgs : RoutedEventArgs
{
    public ExecutedRoutedEventArgs(RoutedCommand command, object parameter)
    {
        Command = command;
        Parameter = parameter;
    }

    public RoutedCommand Command { get; }

    public object Parameter { get; }

    internal bool Ran { get; set; }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is ExecutedRoutedEventHandler typed)
        {
            typed(target, this);
            return;
        }

        base.InvokeEventHandler(handler, target);
    }
}
