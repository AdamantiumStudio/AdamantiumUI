using System;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Data;

/// <summary>The live connection behind <c>{ObservableResource Key}</c>: resolves a keyed resource tree-scoped (Local, then
/// Theme, then Global) and re-resolves on any resource-set change or re-parent - and, with a key taken from a binding,
/// whenever the binding names another one. A miss of the key already shown keeps its value (a theme swap passes through
/// one); a new key that resolves to nothing, or no key, clears it.</summary>
public class ObservableResourceExpression : BindingExpressionBase
{
    private readonly string _key;
    private readonly BindingBase _keySource;
    private readonly ValuePriority _priority;
    private readonly object _token;
    private IResourceManager _resources;
    private BindingExpressionBase _followedKey;
    private IFundamentalUIComponent _element;
    private string _shownKey;

    public ObservableResourceExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty, string key,
        ValuePriority priority = ValuePriority.Template, object token = null, BindingBase keySource = null)
    {
        Target = target;
        TargetProperty = targetProperty;
        _key = key;
        _keySource = keySource;
        _priority = priority;
        _token = token;
    }

    public override void EstablishConnection()
    {
        CloseConnection();
        _resources = UIAppContext.Current?.ResourceManager;
        if (_resources == null || TargetProperty == null || (_keySource == null && string.IsNullOrEmpty(_key))) return;

        _element = DataContextSource;

        if (_keySource != null)
        {
            FollowKey();
            if (_element != null)
            {
                _element.DataContextChanged += OnDataContextChanged;
            }
        }

        UpdateTarget();
        Listen(_resources, this);

        if (_element is IUIComponent visual) visual.AttachedToVisualTreeEvent += OnVisualAttached;
        else if (_element != null) _element.AttachedToLogicalTree += OnLogicalAttached;

        if (Target is IInputComponent input) input.Unloaded += OnTargetUnloaded;
    }

    public override void UpdateTarget()
    {
        if (_resources == null || TargetProperty == null) return;

        var key = CurrentKey();
        var value = string.IsNullOrEmpty(key) ? null : _resources.FindResource(DataContextSource, key);
        if (value == null)
        {
            if (key != _shownKey) ClearShown();
            return;
        }

        _shownKey = key;
        if (_priority == ValuePriority.Trigger && _token != null)
            Target.SetTriggerValue(TargetProperty, value, _token);
        else if (Style.GetContributingStyle(_priority, _token) is { } style)
            Target.SetStyleValue(TargetProperty, value, style);
        else
            Target.SetValue(TargetProperty, value, _priority);
    }

    public override void CloseConnection()
    {
        UnfollowKey();

        if (_resources != null)
        {
            Unlisten(_resources, this);
            _resources = null;
        }

        if (_element != null)
        {
            _element.DataContextChanged -= OnDataContextChanged;
            if (_element is IUIComponent visual) visual.AttachedToVisualTreeEvent -= OnVisualAttached;
            else _element.AttachedToLogicalTree -= OnLogicalAttached;
            _element = null;
        }

        if (Target is IInputComponent input) input.Unloaded -= OnTargetUnloaded;
        if (Target is IUIComponent returning) returning.AttachedToVisualTreeEvent -= OnReturned;
    }

    private string CurrentKey() => _keySource != null ? _followedKey?.ProducedValue?.ToString() : _key;

    private void ClearShown()
    {
        if (_shownKey == null) return;

        _shownKey = null;
        if (_priority == ValuePriority.Trigger && _token != null)
            Target.ClearTriggerValue(TargetProperty, _token);
        else if (Style.GetContributingStyle(_priority, _token) is { } style)
            Target.RemoveStyleValue(TargetProperty.Name, style);
        else
            Target.ClearValue(TargetProperty, _priority);
    }

    private void FollowKey()
    {
        UnfollowKey();
        _followedKey = _keySource.CreateExpression(Target, null);
        _followedKey.ProducesText = true;
        _followedKey.ValueChanged += OnKeyChanged;
        _followedKey.EstablishConnection();
    }

    private void UnfollowKey()
    {
        if (_followedKey == null)
        {
            return;
        }

        _followedKey.ValueChanged -= OnKeyChanged;
        _followedKey.CloseConnection();
        _followedKey = null;
    }

    private void OnDataContextChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        FollowKey();
        UpdateTarget();
    }

    // ---- Listening ----------------------------------------------------------------------------------------------------
    // One subscription per resource manager, listeners held WEAKLY (no Unloaded closes a drawing's), in the order they came.
    private sealed class Listeners
    {
        public readonly LinkedList<WeakReference<ObservableResourceExpression>> All = new();
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IResourceManager, Listeners> ListenersOf = new();
    private static readonly object ListenersLock = new();

    // This expression's place in its manager's list, so it leaves in O(1).
    private LinkedListNode<WeakReference<ObservableResourceExpression>> _listening;

    private static void Listen(IResourceManager resources, ObservableResourceExpression expression)
    {
        lock (ListenersLock)
        {
            if (!ListenersOf.TryGetValue(resources, out var listeners))
            {
                ListenersOf.Add(resources, listeners = new Listeners());
                resources.ResourcesChanged += OnResourcesChanged;
            }

            // One node for the expression's whole life: connecting again allocates nothing.
            expression._listening ??= new LinkedListNode<WeakReference<ObservableResourceExpression>>(new(expression));
            if (expression._listening.List == null) listeners.All.AddLast(expression._listening);
        }
    }

    /// <summary>How many expressions listen to <paramref name="resources"/>, those collected but not yet swept included.</summary>
    internal static int ListenerCount(IResourceManager resources)
    {
        lock (ListenersLock) return ListenersOf.TryGetValue(resources, out var listeners) ? listeners.All.Count : 0;
    }

    private static void Unlisten(IResourceManager resources, ObservableResourceExpression expression)
    {
        lock (ListenersLock)
        {
            if (expression._listening?.List != null) expression._listening.List.Remove(expression._listening);
        }
    }

    private static void OnResourcesChanged(object sender, EventArgs e)
    {
        ObservableResourceExpression[] woken;
        lock (ListenersLock)
        {
            if (sender is not IResourceManager resources || !ListenersOf.TryGetValue(resources, out var listeners)) return;

            // A SNAPSHOT, since updating can connect again; and the sweep of listeners collected meanwhile.
            var alive = new List<ObservableResourceExpression>(listeners.All.Count);
            for (var node = listeners.All.First; node != null;)
            {
                var next = node.Next;
                if (node.Value.TryGetTarget(out var expression)) alive.Add(expression);
                else listeners.All.Remove(node);
                node = next;
            }

            woken = alive.ToArray();
        }

        foreach (var expression in woken) expression.UpdateTarget();
    }

    private void OnKeyChanged(BindingExpressionBase key) => UpdateTarget();

    private void OnVisualAttached(object sender, VisualTreeAttachmentEventArgs e) => UpdateTarget();

    private void OnLogicalAttached(object sender, LogicalTreeAttachmentEventArgs e) => UpdateTarget();

    private void OnTargetUnloaded(object sender, RoutedEventArgs e)
    {
        CloseConnection();
        if (Target is IUIComponent visual) visual.AttachedToVisualTreeEvent += OnReturned;
    }

    private void OnReturned(object sender, VisualTreeAttachmentEventArgs e) => EstablishConnection();
}
