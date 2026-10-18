namespace Adamantium.UI.Core.Input;

/// <summary>What the element a <see cref="RoutedCommand"/> reaches says of it now: whether it can run, whether it is on
/// (a toggle - bold where the caret is), and its value (a choice - the font there); null where it does not say.</summary>
public readonly record struct CommandState(bool CanExecute, bool? IsChecked = null, object Value = null)
{
    /// <summary>No element on the way handles the command.</summary>
    public static CommandState Unhandled { get; } = new(false);
}
