using System.Buffers;
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
        "InputBindings", typeof(KeyBindingCollection), typeof(AdamantiumComponent), new PropertyMetadata(null));

    private static readonly AdamantiumProperty KeyHostsProperty = AdamantiumProperty.RegisterAttached(
        "KeyHosts", typeof(List<IUIComponent>), typeof(AdamantiumComponent), new PropertyMetadata(null));

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
    private static bool windowKeysRegistered;
    private static readonly object HostsLock = new();

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
    public static KeyBindingCollection GetInputBindings(IAdamantiumComponent element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetValue(InputBindingsProperty) is not KeyBindingCollection bindings)
        {
            bindings = new KeyBindingCollection(element);
            element.SetValue(InputBindingsProperty, bindings);
        }

        return bindings;
    }

    /// <summary>Whether a key would run a binding set anywhere in the window of <paramref name="from"/> but on it - around
    /// it as well: what an element that takes every key, a hosted universe, lets go on.</summary>
    public static bool IsBoundInWindow(IUIComponent from, KeyEventArgs e) => FindInWindow(from, e, aroundToo: true) != null;

    internal static void List(IRootVisualComponent root, IUIComponent host)
    {
        if (root == null)
        {
            return;
        }

        lock (HostsLock)
        {
            if (root.GetValue(KeyHostsProperty) is not List<IUIComponent> hosts)
            {
                hosts = [];
                root.SetValue(KeyHostsProperty, hosts);
            }

            hosts.Add(host);
        }
    }

    internal static void Unlist(IRootVisualComponent root, IUIComponent host)
    {
        if (root == null)
        {
            return;
        }

        lock (HostsLock)
        {
            if (root.GetValue(KeyHostsProperty) is List<IUIComponent> hosts)
            {
                hosts.Remove(host);
            }
        }
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
    public static void ExecuteFrom(ICommand command, object parameter, IInputComponent source, IInputComponent target) =>
        ExecuteFrom(command, parameter, source, target, true);

    /// <summary>Runs a command source's command when it can run; with <paramref name="returnsFocus"/> false the focus
    /// stays where it is, as in a number box stepped from the keyboard, which goes on taking steps.</summary>
    public static void ExecuteFrom(ICommand command, object parameter, IInputComponent source, IInputComponent target,
        bool returnsFocus)
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

        if (ran && returnsFocus)
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
        if (RunKey((sender as IAdamantiumComponent)?.GetValue(InputBindingsProperty) as KeyBindingCollection, e, target) ||
            ForClass(ClassInputBindings, type, e, target, RunKey) ||
            RunGesture(Own<CommandBinding>(sender, CommandBindingsProperty), e, target) ||
            ForClass(ClassCommandBindings, type, e, target, RunGesture))
        {
            e.Handled = true;
        }
    }

    /// <summary>Hooks the key handler that runs, at a window's root and after everything on the way up - its default and
    /// cancel buttons and the keyboard navigation too - the binding set anywhere in the window for a key nobody took.
    /// Called from the input element base's static constructor after the navigation hooks its own.</summary>
    public static void RegisterWindowKeys()
    {
        if (windowKeysRegistered)
        {
            return;
        }

        windowKeysRegistered = true;
        Keyboard.KeyDownEvent.RegisterClassHandler<IInputComponent>(new KeyEventHandler(OnKeyLeftOver));
    }

    private static void OnKeyLeftOver(object sender, KeyEventArgs e)
    {
        if (!e.Handled && sender is IRootVisualComponent root && FindInWindow(root, e) is { } found)
        {
            e.Handled = Run(found.Binding, found.Host);
        }
    }

    private readonly record struct WindowKey(KeyBinding Binding, IUIComponent Host);

    private static WindowKey? FindInWindow(IUIComponent from, KeyEventArgs e, bool aroundToo = false)
    {
        var root = from as IRootVisualComponent ?? from?.RootVisual;
        if (root == null || e.IsRepeated || TypesInto(FocusManager.Focused, e))
        {
            return null;
        }

        IUIComponent[] hosts;
        int count;
        lock (HostsLock)
        {
            if (root.GetValue(KeyHostsProperty) is not List<IUIComponent> { Count: > 0 } listed)
            {
                return null;
            }

            count = listed.Count;
            hosts = ArrayPool<IUIComponent>.Shared.Rent(count);
            listed.CopyTo(hosts);
        }

        try
        {
            var focusChain = new List<IUIComponent>();
            for (var node = e.OriginalSource as IUIComponent ?? from; node != null; node = node.VisualParent)
            {
                focusChain.Add(node);
            }

            WindowKey? best = null;
            var bestDepth = -1;
            for (var i = 0; i < count; i++)
            {
                var host = hosts[i];
                if (host.GetValue(InputBindingsProperty) is not KeyBindingCollection { Count: > 0 } bindings ||
                    (aroundToo ? ReferenceEquals(host, from) : focusChain.Contains(host)) || !IsLive(host, root))
                {
                    continue;
                }

                foreach (var binding in bindings)
                {
                    if (!binding.Gesture.Matches(e.Key, e.Modifiers) || !CanRun(binding, host))
                    {
                        continue;
                    }

                    var depth = SharedDepth(host, focusChain);
                    if (best == null || depth > bestDepth || (depth == bestDepth && FirstInTree(host, best.Value.Host)))
                    {
                        best = new WindowKey(binding, host);
                        bestDepth = depth;
                    }

                    break;
                }
            }

            return best;
        }
        finally
        {
            ArrayPool<IUIComponent>.Shared.Return(hosts, clearArray: true);
        }
    }

    private static bool TypesInto(IInputComponent focused, KeyEventArgs e)
    {
        if (e.Key is >= Key.F1 and <= Key.F24 || !InTextEntry(focused))
        {
            return false;
        }

        var held = KeyGesture.Of(e.Modifiers) & ~ModifierKeys.Shift;
        return held is ModifierKeys.None or (ModifierKeys.Control | ModifierKeys.Alt);
    }

    private static bool InTextEntry(IInputComponent focused)
    {
        for (IUIComponent node = focused; node != null; node = node.VisualParent)
        {
            if (node is ITextEntry)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLive(IUIComponent host, IRootVisualComponent root)
    {
        if (!ReferenceEquals(host.RootVisual, root) && !ReferenceEquals(host, root))
        {
            return false;
        }

        for (var node = host; node != null; node = node.VisualParent)
        {
            if (node.Visibility != Visibility.Visible || !node.IsEnabled)
            {
                return false;
            }
        }

        return true;
    }

    private static int SharedDepth(IUIComponent host, List<IUIComponent> focusChain)
    {
        for (var node = host; node != null; node = node.VisualParent)
        {
            var index = focusChain.IndexOf(node);
            if (index >= 0)
            {
                return focusChain.Count - 1 - index;
            }
        }

        return -1;
    }

    private static bool FirstInTree(IUIComponent a, IUIComponent b)
    {
        var chainA = new List<IUIComponent>();
        for (var node = a; node != null; node = node.VisualParent)
        {
            chainA.Insert(0, node);
        }

        var chainB = new List<IUIComponent>();
        for (var node = b; node != null; node = node.VisualParent)
        {
            chainB.Insert(0, node);
        }

        var i = 0;
        while (i < chainA.Count && i < chainB.Count && ReferenceEquals(chainA[i], chainB[i]))
        {
            i++;
        }

        if (i == chainA.Count || i == chainB.Count || i == 0)
        {
            return i == chainA.Count;
        }

        foreach (var child in chainA[i - 1].VisualChildren)
        {
            if (ReferenceEquals(child, chainA[i]))
            {
                return true;
            }

            if (ReferenceEquals(child, chainB[i]))
            {
                return false;
            }
        }

        return false;
    }

    private static bool CanRun(KeyBinding binding, IUIComponent host) => binding.Command switch
    {
        null => false,
        RoutedCommand routed => Query(routed, binding.CommandParameter, host as IInputComponent).CanExecute,
        { } command => command.CanExecute(binding.CommandParameter)
    };

    private static bool Run(KeyBinding binding, IUIComponent host)
    {
        if (binding.Command is RoutedCommand routed)
        {
            return Execute(routed, binding.CommandParameter, host as IInputComponent);
        }

        binding.Command.Execute(binding.CommandParameter);
        return true;
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

    private static bool RunKey(IEnumerable<KeyBinding> bindings, KeyEventArgs e, IInputComponent target)
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
