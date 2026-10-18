using Adamantium.Core.Commands;

namespace Adamantium.UI.Core.Input;

/// <summary>A key that runs a command while the focus is on the element it is set on or inside it
/// (<see cref="CommandManager.GetInputBindings"/>), or on any element of a class
/// (<see cref="CommandManager.RegisterClassInputBinding"/>). A <see cref="RoutedCommand"/> goes from the focus.</summary>
public class KeyBinding
{
    public KeyBinding()
    {
    }

    public KeyBinding(ICommand command, KeyGesture gesture, object commandParameter = null)
    {
        Command = command;
        Gesture = gesture;
        CommandParameter = commandParameter;
    }

    public ICommand Command { get; set; }

    public KeyGesture Gesture { get; set; }

    public object CommandParameter { get; set; }
}
