using Adamantium.Core.Commands;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

/// <summary>Takes a <see cref="RoutedCommand"/> from its target up the tree to the first element that handles it, runs
/// the keys that ask for commands, and tells command sources when to ask again. A focus scope that nothing inside took
/// a command from hands it to the element with the focus in the scope around it - a ribbon's Copy goes to the field being
/// edited - and a popup's content goes on to the element the popup belongs to.</summary>
public static class CommandManager
{
    private const int MostHops = 16;

    public static readonly RoutedEvent PreviewCanExecuteEvent = EventManager.RegisterRoutedEvent("PreviewCanExecute",
        RoutingStrategy.Tunnel, typeof(CanExecuteRoutedEventHandler), typeof(CommandManager));

    public static readonly RoutedEvent CanExecuteEvent = EventManager.RegisterRoutedEvent("CanExecute",
        RoutingStrategy.Bubble, typeof(CanExecuteRoutedEventHandler), typeof(CommandManager));

    public static readonly RoutedEvent PreviewExecutedEvent = EventManager.RegisterRoutedEvent("PreviewExecuted",
        RoutingStrategy.Tunnel, typeof(ExecutedRoutedEventHandler), typeof(CommandManager));

    public static readonly RoutedEvent ExecutedEvent = EventManager.RegisterRoutedEvent("Executed",
        RoutingStrategy.Bubble, typeof(ExecutedRoutedEventHandler), typeof(CommandManager));

    public static readonly AdamantiumProperty CommandBindingsProperty = AdamantiumProperty.RegisterAttached(
        "CommandBindings", typeof(List<CommandBinding>), typeof(AdamantiumComponent), new PropertyMetadata(null));

    public static readonly AdamantiumProperty InputBindingsProperty = AdamantiumProperty.RegisterAttached(
        "InputBindings", typeof(List<KeyBinding>), typeof(AdamantiumComponent), new PropertyMetadata(null));

    /// <summary>Where the commands of every source in this element's subtree go when nothing on their way takes them -
    /// no field with the focus, or one that knows nothing of them - and the source names no target of its own: a
    /// ribbon's buttons act on the document beside it before anything had the focus. Inherited.</summary>
    public static readonly AdamantiumProperty CommandTargetProperty = AdamantiumProperty.RegisterAttached(
        "CommandTarget", typeof(IInputComponent), typeof(AdamantiumComponent),
        new PropertyMetadata(null, PropertyMetadataOptions.Inherits, OnCommandTargetChanged));

    // Elements a source sends its commands to whatever the focus: what they answer changes without the focus on them.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IUIComponent, object> NamedTargets = new();

    private static readonly Dictionary<Type, List<CommandBinding>> ClassCommandBindings = new();
    private static readonly Dictionary<Type, List<KeyBinding>> ClassInputBindings = new();
    private static readonly Func<double, bool> RequeryOnFrame = _ =>
    {
        RequeryNow();
        return true;
    };

    private static bool requeryPending;
    private static long requeryAskedOnTick;

    [ThreadStatic]
    private static int transfers;

    private static bool registered;

    static CommandManager() => Register();

    /// <summary>Hooks the command and key handlers. Called from the input element base's static constructor before it
    /// hooks its own key handler, so a key bound to a command reaches the command before the control's own
    /// <c>OnKeyDown</c>.</summary>
    public static void Register()
    {
        if (registered)
        {
            return;
        }

        registered = true;
        CanExecuteEvent.RegisterClassHandler<IObservableComponent>(new CanExecuteRoutedEventHandler(OnCanExecute));
        ExecutedEvent.RegisterClassHandler<IObservableComponent>(new ExecutedRoutedEventHandler(OnExecuted));
        Keyboard.KeyDownEvent.RegisterClassHandler<IInputComponent>(new KeyEventHandler(OnKeyDown));
    }

    /// <summary>Raised when command sources should ask their commands again: after
    /// <see cref="InvalidateRequerySuggested"/>, at most once a frame.</summary>
    public static event EventHandler RequerySuggested;

    /// <summary>Where a command goes when neither a target nor a source is named: the keyboard focus.</summary>
    public static IInputComponent DefaultTarget => FocusManager.Focused;

    /// <summary>Has command sources ask again on the next frame - what a control calls when what it would answer has
    /// changed, as a selection that becomes empty. Costs nothing while no source listens.</summary>
    public static void InvalidateRequerySuggested()
    {
        // Pending since an earlier frame means its ticker was dropped (AnimationManager.Reset): ask for it again.
        if (RequerySuggested == null || (requeryPending && requeryAskedOnTick == AnimationManager.Ticks))
        {
            return;
        }

        requeryPending = true;
        requeryAskedOnTick = AnimationManager.Ticks;
        AnimationManager.AddTicker(RequeryOnFrame);
    }

    /// <summary>What a control calls when what it would answer has changed: sources ask again when it is the element
    /// with the focus in its scope, where commands from around it go.</summary>
    public static void InvalidateRequerySuggested(IUIComponent changed)
    {
        if (changed is IInputComponent element &&
            (ReferenceEquals(FocusManager.GetFocusedElement(FocusManager.GetFocusScope(changed)), element) ||
             NamedTargets.TryGetValue(changed, out _)))
        {
            InvalidateRequerySuggested();
        }
    }

    public static IInputComponent GetCommandTarget(IAdamantiumComponent element) =>
        element?.GetValue(CommandTargetProperty) as IInputComponent;

    public static void SetCommandTarget(IAdamantiumComponent element, IInputComponent value) =>
        element?.SetValue(CommandTargetProperty, value);

    private static void OnCommandTargetChanged(AdamantiumComponent element, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.NewValue is IUIComponent target)
        {
            NamedTargets.TryAdd(target, null);
        }

        InvalidateRequerySuggested();
    }

    /// <summary>Has command sources ask again at once - what a menu does as it opens, so its rows say what holds that
    /// moment: the clipboard changes without telling anyone.</summary>
    public static void RequeryNow()
    {
        requeryPending = false;
        RequerySuggested?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The bindings set on <paramref name="element"/> itself, made on first ask.</summary>
    public static List<CommandBinding> GetCommandBindings(IAdamantiumComponent element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetValue(CommandBindingsProperty) is not List<CommandBinding> bindings)
        {
            bindings = [];
            element.SetValue(CommandBindingsProperty, bindings);
        }

        return bindings;
    }

    /// <summary>The keys set on <paramref name="element"/> itself, made on first ask.</summary>
    public static List<KeyBinding> GetInputBindings(IAdamantiumComponent element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetValue(InputBindingsProperty) is not List<KeyBinding> bindings)
        {
            bindings = [];
            element.SetValue(InputBindingsProperty, bindings);
        }

        return bindings;
    }

    /// <summary>Has every element of <paramref name="type"/> and of the types derived from it handle a command; an
    /// element's own binding goes first.</summary>
    public static void RegisterClassCommandBinding(Type type, CommandBinding binding)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(binding);
        if (!ClassCommandBindings.TryGetValue(type, out var bindings))
        {
            ClassCommandBindings[type] = bindings = [];
        }

        bindings.Add(binding);
    }

    /// <summary>Has a key run a command while the focus is on or inside any element of <paramref name="type"/>.</summary>
    public static void RegisterClassInputBinding(Type type, KeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(binding);
        if (!ClassInputBindings.TryGetValue(type, out var bindings))
        {
            ClassInputBindings[type] = bindings = [];
        }

        bindings.Add(binding);
    }

    /// <summary>Where a command from <paramref name="source"/> starts: <paramref name="target"/> when it names one, else
    /// the source itself - from a ribbon or a menu it goes on to the field with the focus - else
    /// <see cref="DefaultTarget"/>. What nothing on that way takes goes to the <see cref="CommandTargetProperty"/>
    /// around the source.</summary>
    public static IInputComponent TargetFor(IInputComponent source, IInputComponent target) =>
        target ?? source ?? DefaultTarget;

    /// <summary>What a command source - a button, a menu row - shows of its command: a routed one is asked of its
    /// target, and failing it of the <see cref="CommandTargetProperty"/> around the source; any other of itself.</summary>
    public static CommandState QueryFrom(ICommand command, object parameter, IInputComponent source,
        IInputComponent target)
    {
        if (command is not RoutedCommand routed)
        {
            return new CommandState(command?.CanExecute(parameter) ?? true);
        }

        var answer = Ask(routed, parameter, TargetFor(source, target)) ??
                     (FallbackFor(source, target) is { } fallback ? Ask(routed, parameter, fallback) : null);
        return answer is { } asked ? new CommandState(asked.CanExecute, asked.IsChecked, asked.Value) : CommandState.Unhandled;
    }

    /// <summary>Runs a command source's command when it can run; from a scope that
    /// <see cref="FocusManager.ReturnsFocusProperty"/>, the focus then goes back to the field it acted on - or, when the
    /// <see cref="CommandTargetProperty"/> took it, to that target.</summary>
    public static void ExecuteFrom(ICommand command, object parameter, IInputComponent source, IInputComponent target)
    {
        var from = source as IUIComponent;
        var field = from != null ? FocusManager.FocusToReturnTo(from) : null;
        var before = FocusManager.Focused;
        bool ran;
        if (command is RoutedCommand routed)
        {
            var run = Run(routed, parameter, TargetFor(source, target));
            if (run == null && FallbackFor(source, target) is { } fallback)
            {
                run = Run(routed, parameter, fallback);
                if (run?.Ran == true && from != null && FocusManager.ReturnsFocusFrom(from))
                {
                    field = fallback;
                }
            }

            ran = run?.Ran == true;
        }
        else if (command != null && command.CanExecute(parameter))
        {
            command.Execute(parameter);
            ran = true;
        }
        else
        {
            ran = false;
        }

        if (ran)
        {
            FocusManager.ReturnFocus(field, before);
        }
    }

    // A source's own target leaves no fallback: it said where its commands go.
    private static IInputComponent FallbackFor(IInputComponent source, IInputComponent target) =>
        target == null ? GetCommandTarget(source) : null;

    internal static CommandState Query(RoutedCommand command, object parameter, IInputComponent target) =>
        Ask(command, parameter, target) is { } answer
            ? new CommandState(answer.CanExecute, answer.IsChecked, answer.Value)
            : CommandState.Unhandled;

    internal static bool Execute(RoutedCommand command, object parameter, IInputComponent target) =>
        Run(command, parameter, target)?.Ran == true;

    private static CanExecuteRoutedEventArgs Ask(RoutedCommand command, object parameter, IObservableComponent target)
    {
        for (var hop = 0; target != null && hop < MostHops; hop++, target = Beyond(target))
        {
            var args = new CanExecuteRoutedEventArgs(command, parameter) { RoutedEvent = PreviewCanExecuteEvent };
            target.RaiseEvent(args);
            if (!args.Handled)
            {
                args.RoutedEvent = CanExecuteEvent;
                target.RaiseEvent(args);
            }

            if (args.Handled)
            {
                return args;
            }
        }

        return null;
    }

    private static ExecutedRoutedEventArgs Run(RoutedCommand command, object parameter, IObservableComponent target)
    {
        for (var hop = 0; target != null && hop < MostHops; hop++, target = Beyond(target))
        {
            var args = new ExecutedRoutedEventArgs(command, parameter) { RoutedEvent = PreviewExecutedEvent };
            target.RaiseEvent(args);
            if (!args.Handled)
            {
                args.RoutedEvent = ExecutedEvent;
                target.RaiseEvent(args);
            }

            if (args.Handled)
            {
                return args;
            }
        }

        return null;
    }

    private static IObservableComponent Beyond(IObservableComponent target)
    {
        IUIComponent top = target;
        while (top.VisualParent is { } parent)
        {
            top = parent;
        }

        return (top as IFundamentalUIComponent)?.LogicalParent as IObservableComponent;
    }

    private static IInputComponent HandedOnFrom(object sender)
    {
        if (transfers >= MostHops || sender is not IUIComponent scope || !FocusManager.GetIsFocusScope(scope) ||
            FocusManager.ParentOf(scope) is not { } above)
        {
            return null;
        }

        var focused = FocusManager.GetFocusedElement(FocusManager.GetFocusScope(above));
        for (var node = focused as IUIComponent; node != null; node = FocusManager.ParentOf(node))
        {
            if (ReferenceEquals(node, scope))
            {
                return null;
            }
        }

        return focused;
    }

    private static void OnCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (Find(sender, e.Command) is { } binding)
        {
            binding.Ask(sender, e);
            if (e.Handled)
            {
                return;
            }
        }

        if (HandedOnFrom(sender) is not { } focused)
        {
            return;
        }

        transfers++;
        try
        {
            if (Ask(e.Command, e.Parameter, focused) is { } answer)
            {
                e.CanExecute = answer.CanExecute;
                e.IsChecked = answer.IsChecked;
                e.Value = answer.Value;
                e.Handled = true;
            }
        }
        finally
        {
            transfers--;
        }
    }

    private static void OnExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (Find(sender, e.Command) is { } binding)
        {
            var asked = new CanExecuteRoutedEventArgs(e.Command, e.Parameter);
            binding.Ask(sender, asked);
            if (asked.Handled)
            {
                e.Handled = true;
                if (asked.CanExecute)
                {
                    e.Ran = true;
                    binding.Run(sender, e);
                }

                return;
            }
        }

        if (HandedOnFrom(sender) is not { } focused)
        {
            return;
        }

        transfers++;
        try
        {
            if (Run(e.Command, e.Parameter, focused) is { } ran)
            {
                e.Ran = ran.Ran;
                e.Handled = true;
            }
        }
        finally
        {
            transfers--;
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        var target = e.OriginalSource as IInputComponent ?? sender as IInputComponent;
        var type = sender.GetType();
        if (RunKey(Own<KeyBinding>(sender, InputBindingsProperty), e, target) ||
            ForClass(ClassInputBindings, type, e, target, RunKey) ||
            RunGesture(Own<CommandBinding>(sender, CommandBindingsProperty), e, target) ||
            ForClass(ClassCommandBindings, type, e, target, RunGesture))
        {
            e.Handled = true;
        }
    }

    private static bool ForClass<T>(Dictionary<Type, List<T>> bindings, Type type, KeyEventArgs e,
        IInputComponent target, Func<List<T>, KeyEventArgs, IInputComponent, bool> run)
    {
        for (; type != null; type = type.BaseType)
        {
            if (bindings.TryGetValue(type, out var list) && run(list, e, target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RunKey(List<KeyBinding> bindings, KeyEventArgs e, IInputComponent target)
    {
        if (bindings == null)
        {
            return false;
        }

        foreach (var binding in bindings)
        {
            if (!binding.Gesture.Matches(e.Key, e.Modifiers) || binding.Command == null)
            {
                continue;
            }

            if (binding.Command is RoutedCommand routed)
            {
                return Run(routed, binding.CommandParameter, target) != null;
            }

            if (binding.Command.CanExecute(binding.CommandParameter))
            {
                binding.Command.Execute(binding.CommandParameter);
                return true;
            }
        }

        return false;
    }

    private static bool RunGesture(List<CommandBinding> bindings, KeyEventArgs e, IInputComponent target)
    {
        if (bindings == null)
        {
            return false;
        }

        foreach (var binding in bindings)
        {
            if (binding.Command is not { } command)
            {
                continue;
            }

            foreach (var gesture in command.InputGestures)
            {
                if (gesture.Matches(e.Key, e.Modifiers))
                {
                    return Run(command, null, target) != null;
                }
            }
        }

        return false;
    }

    private static CommandBinding Find(object element, RoutedCommand command)
    {
        if (Own<CommandBinding>(element, CommandBindingsProperty) is { } own && Matching(own, command) is { } binding)
        {
            return binding;
        }

        for (var type = element.GetType(); type != null; type = type.BaseType)
        {
            if (ClassCommandBindings.TryGetValue(type, out var list) && Matching(list, command) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static CommandBinding Matching(List<CommandBinding> bindings, RoutedCommand command)
    {
        foreach (var binding in bindings)
        {
            if (ReferenceEquals(binding.Command, command))
            {
                return binding;
            }
        }

        return null;
    }

    private static List<T> Own<T>(object element, AdamantiumProperty property) =>
        (element as IAdamantiumComponent)?.GetValue(property) as List<T>;
}
