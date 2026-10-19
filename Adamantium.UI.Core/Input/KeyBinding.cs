using Adamantium.Core.Commands;
using Adamantium.UI.Core.Behaviors;

namespace Adamantium.UI.Core.Input;

/// <summary>A key that runs a command. The element it is set on and the elements around the focus have the key first;
/// when nothing on the way up from the focus took it, it runs from anywhere in the element's window, the binding nearest
/// to the focus winning (<see cref="CommandManager"/>). It joins the element's logical tree, so its command and parameter
/// can be bound to the element's data: <c>&lt;KeyBinding Gesture="Escape" Command="{Binding Cancel}"/&gt;</c>.</summary>
public class KeyBinding : FundamentalUIComponent, IAttachedObject
{
    public static readonly AdamantiumProperty CommandProperty = AdamantiumProperty.Register(nameof(Command),
        typeof(ICommand), typeof(KeyBinding), new PropertyMetadata(null));

    public static readonly AdamantiumProperty CommandParameterProperty = AdamantiumProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(KeyBinding), new PropertyMetadata(null));

    public static readonly AdamantiumProperty GestureProperty = AdamantiumProperty.Register(nameof(Gesture),
        typeof(KeyGesture), typeof(KeyBinding), new PropertyMetadata(default(KeyGesture)));

    public KeyBinding()
    {
    }

    public KeyBinding(ICommand command, KeyGesture gesture, object commandParameter = null)
    {
        Command = command;
        Gesture = gesture;
        CommandParameter = commandParameter;
    }

    public ICommand Command
    {
        get => GetValue<ICommand>(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public KeyGesture Gesture
    {
        get => GetValue<KeyGesture>(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    public void AttachTo(IAdamantiumComponent adamantiumComponent)
    {
        if (adamantiumComponent is IFundamentalUIComponent element)
        {
            element.AddLogicalChild(this);
        }
    }

    public void DetachFrom(IAdamantiumComponent adamantiumComponent)
    {
        if (adamantiumComponent is IFundamentalUIComponent element)
        {
            element.RemoveLogicalChild(this);
        }
    }
}
