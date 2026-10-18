using Adamantium.Core.Commands;

namespace Adamantium.UI.Core.Input;

/// <summary>An action by name, with the keys that ask for it; what it does is up to the element it reaches. It goes to
/// its target - one given, or else the keyboard focus - and up the tree to the first element with a
/// <see cref="CommandBinding"/> for it. So a ribbon's Copy copies in whichever field the focus is in.</summary>
public class RoutedCommand : ICommand
{
    public RoutedCommand(string name, Type ownerType, params KeyGesture[] gestures)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(ownerType);
        Name = name;
        OwnerType = ownerType;
        InputGestures = gestures ?? [];
    }

    public string Name { get; }

    public Type OwnerType { get; }

    /// <summary>The keys that run it wherever an element on the way from the focus handles it; the first is the one a
    /// menu row shows.</summary>
    public IReadOnlyList<KeyGesture> InputGestures { get; }

    /// <summary>Raised when what elements say of it may have changed - the focus moved, or one called
    /// <see cref="CommandManager.InvalidateRequerySuggested"/>; at most once a frame.</summary>
    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>What the first element on the way up from <paramref name="target"/> that handles it says of it.</summary>
    public CommandState Query(object parameter, IInputComponent target) => CommandManager.Query(this, parameter, target);

    /// <summary>Runs it on the first element on the way up from <paramref name="target"/> that handles it and can run it
    /// now; false when none did.</summary>
    public bool Execute(object parameter, IInputComponent target) => CommandManager.Execute(this, parameter, target);

    public bool CanExecute(object parameter = null) => Query(parameter, CommandManager.DefaultTarget).CanExecute;

    public void Execute(object parameter = null) => Execute(parameter, CommandManager.DefaultTarget);

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();

    public override string ToString() => $"{OwnerType.Name}.{Name}";
}
