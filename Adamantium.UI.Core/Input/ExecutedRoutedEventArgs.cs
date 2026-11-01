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

    /// <summary>How many times the command was handed on from a focus scope to the one above it on its way here.</summary>
    internal int Transfers { get; init; }
}
