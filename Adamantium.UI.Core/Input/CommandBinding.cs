using Adamantium.Core.Commands;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

/// <summary>What an element does with a <see cref="RoutedCommand"/> that reaches it: set on one element (in its
/// <c>Behaviors</c>, or <see cref="CommandManager.GetCommandBindings"/>) or on a whole class of them
/// (<see cref="CommandManager.RegisterClassCommandBinding"/>). It runs its <see cref="Executed"/> handlers and its
/// <see cref="Handler"/>; without a <see cref="CanExecute"/> handler it can run when the <see cref="Handler"/> can, or
/// when it has <see cref="Executed"/> handlers. A <see cref="CanExecute"/> handler that sets <c>Handled</c> back to
/// false lets the command go on up, as if the element had no binding for it.</summary>
public class CommandBinding : Behavior
{
    public static readonly AdamantiumProperty HandlerProperty = AdamantiumProperty.Register(nameof(Handler),
        typeof(ICommand), typeof(CommandBinding), new PropertyMetadata(null, OnHandlerChanged));

    private WeakCanExecuteChangedRelay<CommandBinding> _handlerRelay;

    public CommandBinding()
    {
    }

    public CommandBinding(RoutedCommand command, ExecutedRoutedEventHandler executed = null,
        CanExecuteRoutedEventHandler canExecute = null)
    {
        Command = command;
        Executed += executed;
        CanExecute += canExecute;
    }

    public RoutedCommand Command { get; set; }

    /// <summary>A command - a view model's - that runs when the binding takes its <see cref="Command"/>, and says when
    /// it can.</summary>
    public ICommand Handler
    {
        get => GetValue(HandlerProperty) as ICommand;
        set => SetValue(HandlerProperty, value);
    }

    public event ExecutedRoutedEventHandler Executed;

    public event CanExecuteRoutedEventHandler CanExecute;

    protected override void OnAttached(IAdamantiumComponent adamantiumComponent)
    {
        CommandManager.GetCommandBindings(adamantiumComponent).Add(this);
        CommandManager.InvalidateRequerySuggested();
    }

    protected override void OnDetached(IAdamantiumComponent adamantiumComponent)
    {
        CommandManager.GetCommandBindings(adamantiumComponent).Remove(this);
        CommandManager.InvalidateRequerySuggested();
    }

    private static void OnHandlerChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var binding = (CommandBinding)a;
        binding._handlerRelay?.Detach();
        binding._handlerRelay = e.NewValue is ICommand handler
            ? new WeakCanExecuteChangedRelay<CommandBinding>(handler, binding,
                static _ => CommandManager.InvalidateRequerySuggested())
            : null;
        CommandManager.InvalidateRequerySuggested();
    }

    internal void Ask(object sender, CanExecuteRoutedEventArgs e)
    {
        e.Handled = true;
        if (CanExecute != null)
        {
            CanExecute(sender, e);
        }
        else
        {
            e.CanExecute = Handler?.CanExecute(e.Parameter) ?? Executed != null;
        }
    }

    internal void Run(object sender, ExecutedRoutedEventArgs e)
    {
        Executed?.Invoke(sender, e);
        Handler?.Execute(e.Parameter);
    }
}
