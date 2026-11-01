using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

/// <summary>A <see cref="RoutedCommand"/> asked about on its way up from its target: a handler that handles it sets
/// <see cref="CanExecute"/>, and may say <see cref="IsChecked"/> and <see cref="Value"/>.</summary>
public class CanExecuteRoutedEventArgs : RoutedEventArgs
{
    public CanExecuteRoutedEventArgs(RoutedCommand command, object parameter)
    {
        Command = command;
        Parameter = parameter;
    }

    public RoutedCommand Command { get; }

    public object Parameter { get; }

    public bool CanExecute { get; set; }

    public bool? IsChecked { get; set; }

    public object Value { get; set; }

    /// <summary>How many times the command was handed on from a focus scope to the one above it on its way here.</summary>
    internal int Transfers { get; init; }
}
